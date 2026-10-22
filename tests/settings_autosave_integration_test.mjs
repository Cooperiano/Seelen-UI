import {test} from 'node:test';
import assert from 'node:assert/strict';
import {build} from 'esbuild';
const entry=`export * from './src/ui/react/settings/state/settingsAutoSave.ts';
export * from './src/ui/react/settings/state/mod.ts';
export * from './src/ui/react/settings/state/componentPreferences.ts';`;
const bundle=await build({stdin:{contents:entry,resolveDir:process.cwd()},bundle:true,write:false,platform:'node',format:'esm',plugins:[{name:'autosave-transport',setup(b){
  b.onResolve({filter:/^@seelen-ui\/lib$/},()=>({path:'transport',namespace:'fixture'}));
  b.onResolve({filter:/^\.\/system$/},()=>({path:'system',namespace:'fixture'}));
  b.onResolve({filter:/^\.\.\/i18n$/},()=>({path:'i18n',namespace:'fixture'}));
  b.onResolve({filter:/^antd$/},()=>({path:'antd',namespace:'fixture'}));
  b.onLoad({filter:/.*/,namespace:'fixture'},({path})=>({resolveDir:process.cwd(),contents:path==='transport'?`
    export const SeelenCommand=new Proxy({},{get:(_,k)=>k});export const SeelenEvent={StateSettingsChanged:'event'};
    export const invoke=(c,a)=>globalThis.autoFixture.invoke(c,a);export const subscribe=(_,fn)=>{globalThis.autoFixture.event=fn;};`
    :path==='system'?`import {signal}from'@preact/signals';export const monitors=signal([]);`
    :path==='i18n'?`export default {changeLanguage(){},t:k=>k};`
    :`export const Modal={confirm:o=>globalThis.autoFixture.confirm(o)};`}));
}}]});
let serial=0;
async function fixture(){
  let disk={language:'en',byApp:[],byWidget:{dock:{delay:800}},monitorsV3:{},wallpaperCollections:[]};
  const data={stats:{data:{HistorySeconds:60},revision:'s1',enabled:false,running:false},
    touchpad:{data:{ThreeFingerDrag:true},revision:'t1',enabled:true,running:true},
    tracking:{data:{Speed:10,Acceleration:true},revision:'p1',enabled:true,running:false}};
  const calls=[],prompts=[];let accept=true,fail=false;
  globalThis.autoFixture={confirm(o){prompts.push(o.title);queueMicrotask(()=>accept?o.onOk():o.onCancel());},async invoke(c,a){
    calls.push({c,a:structuredClone(a)});
    if(c==='StateGetSettingsByApp')return [];
    if(c==='StateGetDefaultMonitorSettings')return {};
    if(c==='StateWriteSettings'){if(fail)throw Error();disk=structuredClone(a.settings);globalThis.autoFixture.event({payload:disk});return;}
    if(c==='StateGetSettings'||c==='StateGetDefaultSettings')return structuredClone(disk);
    const id=c.includes('Tracking')?'tracking':a.module;
    if(c==='SaveDesktopSuitePreferences')data[id]={...data[id],data:{...data[id].data,...a.patch},revision:'new'};
    if(c==='SaveDesktopSuiteTracking')data[id]={...data[id],data:structuredClone(a.desired),revision:'new'};
    return structuredClone(data[id]);
  }};
  const e=await import('data:text/javascript;base64,'+Buffer.from(bundle.outputFiles[0].text+'\n//'+serial++).toString('base64'));
  e.startSettingsAutoSave();e.startSettingsAutoSave();
  return {e,calls,prompts,data,disk:()=>disk,decline(){accept=false;},fail(){fail=true;},recover(){fail=false;}};
}
const settle=()=>new Promise(r=>setTimeout(r,340));
test('actual Suite state autosaves a changed primary setting, with no Save click or restart',async()=>{
  const f=await fixture();await settle();assert.equal(f.calls.filter(c=>c.c==='StateWriteSettings').length,0);
  f.e.settings.value={...f.e.settings.value,byWidget:{dock:{delay:150}}};await settle();
  assert.equal(f.disk().byWidget.dock.delay,150);assert.equal(f.e.autoSaveStatus.value,'saved');
  assert.equal(f.calls.filter(c=>c.c==='StateWriteSettings').length,1);
});
test('disabled Stats settings autosave without starting it; only running Touchpad prompts for restart',async()=>{
  const f=await fixture();await f.e.loadComponentPreferences('stats');await f.e.loadComponentPreferences('touchpad');
  f.e.editComponentPreference('stats','HistorySeconds',90);await settle();
  assert.equal(f.data.stats.data.HistorySeconds,90);assert.equal(f.data.stats.enabled,false);assert.deepEqual(f.prompts,[]);
  f.e.editComponentPreference('touchpad','ThreeFingerDrag',false);await settle();
  assert.equal(f.data.touchpad.data.ThreeFingerDrag,false);assert.deepEqual(f.prompts,['suite_autosave.touchpad_confirm']);
});
test('declining shared acceleration retains its original value while speed-only edits autosave',async()=>{
  const f=await fixture();await f.e.loadComponentPreferences('tracking');f.decline();
  f.e.editComponentPreference('tracking','Speed',12);f.e.editComponentPreference('tracking','Acceleration',false);await settle();
  assert.equal(f.data.tracking.data.Acceleration,true);assert.equal(f.data.tracking.data.Speed,12);
  assert.equal(f.e.componentPreferencesDirty.value,false);assert.equal(f.e.autoSaveStatus.value,'saved');
  assert.equal(f.calls.find(c=>c.c==='SaveDesktopSuiteTracking').a.confirmed,false);
});
test('persistence failure remains visible and explicit retry completes without losing the draft',async()=>{
  const f=await fixture();f.fail();f.e.settings.value={...f.e.settings.value,language:'zh-CN'};await settle();
  assert.equal(f.e.autoSaveStatus.value,'error');assert.equal(f.e.hasChanges.value,true);f.recover();
  assert.equal(await f.e.settingsAutoSave.flush(),true);assert.equal(f.disk().language,'zh-CN');
});
test('acceleration applies only with confirmation; declined Touchpad restart does not write',async()=>{
  const f=await fixture();await f.e.loadComponentPreferences('tracking');
  f.e.editComponentPreference('tracking','Acceleration',false);await settle();
  assert.equal(f.data.tracking.data.Acceleration,false);assert.equal(f.calls.find(c=>c.c==='SaveDesktopSuiteTracking').a.confirmed,true);
  await f.e.loadComponentPreferences('touchpad');f.decline();f.e.editComponentPreference('touchpad','ThreeFingerDrag',false);await settle();
  assert.equal(f.data.touchpad.data.ThreeFingerDrag,true);assert.equal(f.e.autoSaveStatus.value,'error');
  f.e.discardComponentPreferences('touchpad');assert.equal(f.e.componentPreferencesDirty.value,false);
});
