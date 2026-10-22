import { test } from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
import './settings_primary_test.mjs';
import './settings_autosave_test.mjs';
import './settings_autosave_integration_test.mjs';

// Exercise the real shared editor, replacing only the OS command transport.
const bundled=await build({entryPoints:['src/ui/react/settings/state/componentPreferences.ts'],bundle:true,write:false,
  platform:'node',format:'esm',plugins:[{name:'isolated-native-transport',setup(builder){
    builder.onResolve({filter:/^@seelen-ui\/lib$/},()=>({path:'transport',namespace:'fixture'}));
    builder.onLoad({filter:/.*/,namespace:'fixture'},()=>({contents:`
      export const SeelenCommand={GetDesktopSuitePreferences:'get',SaveDesktopSuitePreferences:'save',GetDesktopSuiteTracking:'readTracking',SaveDesktopSuiteTracking:'writeTracking'};
      export async function invoke(command,args){return globalThis.suiteEditorFixture.invoke(command,args);}` }));
  }}]});
let serial=0;
async function fixture(){
  const calls=[];let fail=false;
  const data={stats:{data:{LowPowerMode:false,HistorySeconds:60},revision:'s1',enabled:false,running:false},
    touchpad:{data:{ThreeFingerDrag:true,FourFingerGestures:false},revision:'t1',enabled:true,running:true},
    tracking:{data:{Speed:20,Acceleration:true},revision:'p1',enabled:true,running:false}};
  globalThis.suiteEditorFixture={async invoke(command,args){
    calls.push({command,args:structuredClone(args)});
    if((command==='save'||command==='writeTracking')&&fail)throw new Error('save rejected');
    const id=command==='readTracking'||command==='writeTracking'?'tracking':args.module;
    if(command==='save')data[id]={...data[id],data:{...data[id].data,...args.patch},revision:'new'};
    if(command==='writeTracking')data[id]={...data[id],data:args.desired,revision:'new'};
    return structuredClone(data[id]);
  }};
  const editor=await import('data:text/javascript;base64,'+Buffer.from(bundled.outputFiles[0].text+'\n// '+serial++).toString('base64'));
  return {editor,calls,data,failSave(){fail=true;}};
}
test('reading has no writes and navigation retains drafts across component pages',async()=>{
  const {editor:e,calls}=await fixture();
  await e.loadComponentPreferences('stats');e.editComponentPreference('stats','LowPowerMode',true);
  await e.loadComponentPreferences('touchpad');await e.loadComponentPreferences('stats');
  assert.equal(e.componentEditors.value.stats.draft.LowPowerMode,true);
  assert.equal(e.componentPreferencesDirty.value,true);
  assert.deepEqual(calls.map(c=>c.command),['get','get']);
});
test('cancel restores every loaded draft without writing or enabling anything',async()=>{
  const {editor:e,calls}=await fixture();
  await e.loadComponentPreferences('stats');await e.loadComponentPreferences('tracking');
  e.editComponentPreference('stats','HistorySeconds',90);e.editComponentPreference('tracking','Speed',10);
  e.discardComponentPreferences();
  assert.equal(e.componentPreferencesDirty.value,false);
  assert.equal(e.componentEditors.value.tracking.draft.Speed,20);
  assert.deepEqual(calls.map(c=>c.command),['get','readTracking']);
});
test('disabled component settings save without a component-enable command',async()=>{
  const {editor:e,calls}=await fixture();await e.loadComponentPreferences('stats');
  e.editComponentPreference('stats','HistorySeconds',90);assert.equal(await e.saveComponentPreferences(),true);
  assert.equal(e.componentEditors.value.stats.snapshot.enabled,false);
  assert.equal(e.componentPreferencesDirty.value,false);
  assert.deepEqual(calls[1],{command:'save',args:{module:'stats',revision:'s1',patch:{HistorySeconds:90}}});
});
test('failed save retains original snapshot, dirty draft and visible failure state',async()=>{
  const {editor:e,failSave}=await fixture();await e.loadComponentPreferences('touchpad');
  e.editComponentPreference('touchpad','ThreeFingerDrag',false);failSave();
  assert.equal(await e.saveComponentPreferences(),false);
  assert.equal(e.componentEditors.value.touchpad.snapshot.data.ThreeFingerDrag,true);
  assert.equal(e.componentEditors.value.touchpad.draft.ThreeFingerDrag,false);
  assert.equal(e.componentEditors.value.touchpad.error,true);
  assert.equal(e.componentPreferencesDirty.value,true);assert.equal(e.componentSavePending.value,false);
  assert.equal(e.componentPreferencesFailure.value,'touchpad');
});
test('tracking save carries the original observation and separate confirmation',async()=>{
  const {editor:e,calls}=await fixture();await e.loadComponentPreferences('tracking');
  e.editComponentPreference('tracking','Acceleration',false);assert.equal(e.trackingAccelerationChanged.value,true);
  assert.equal(await e.saveComponentPreferences(true),true);
  assert.deepEqual(calls[1],{command:'writeTracking',args:{expected:{Speed:20,Acceleration:true},desired:{Speed:20,Acceleration:false},confirmed:true}});
  assert.equal(e.trackingAccelerationChanged.value,false);
});
