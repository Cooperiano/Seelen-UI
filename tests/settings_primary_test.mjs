import { test } from 'node:test';
import assert from 'node:assert/strict';
import { build } from 'esbuild';
const bundle = await build({entryPoints:['src/ui/react/settings/state/mod.ts'],bundle:true,write:false,platform:'node',format:'esm',plugins:[{name:'settings-native-fixture',setup(b){
  b.onResolve({filter:/^@seelen-ui\/lib$/},()=>({path:'transport',namespace:'fixture'}));
  b.onResolve({filter:/^\.\/system$/},()=>({path:'system',namespace:'fixture'}));
  b.onResolve({filter:/^\.\.\/i18n$/},()=>({path:'i18n',namespace:'fixture'}));
  b.onResolve({filter:/^antd$/},()=>({path:'antd',namespace:'fixture'}));
  b.onLoad({filter:/.*/,namespace:'fixture'},({path})=>({contents:path==='transport'?`
    export const SeelenCommand={StateGetDefaultSettings:'default',StateGetSettings:'read',StateGetSettingsByApp:'apps',StateGetDefaultMonitorSettings:'monitor',StateWriteSettings:'write'};
    export const SeelenEvent={StateSettingsChanged:'changed'};
    export const invoke=(c,a)=>globalThis.primaryFixture.invoke(c,a);
    export const subscribe=(c,fn)=>{globalThis.primaryFixture.event=fn;};`
    :path==='system'?`import {signal} from '@preact/signals';export const monitors=signal([]);`
    :path==='i18n'?`export default {changeLanguage(){}};`
    :`export const Modal={error(){}};`,resolveDir:process.cwd()}));
}}]});
let serial=0;
async function fixture(){
  let disk={language:'en',byApp:[],byWidget:{dock:{delay:800,enabled:true}},monitorsV3:{},wallpaperCollections:[]};
  let reject=false,release,started;
  const entered=new Promise(r=>started=r);let block=false;
  globalThis.primaryFixture={async invoke(c,a){
    if(c==='apps')return [];
    if(c==='monitor')return {};
    if(c==='write'){
      started();if(block)await new Promise(r=>release=r);
      if(reject)throw Error('disk full');disk=structuredClone(a.settings);
      globalThis.primaryFixture.event({payload:structuredClone(disk)});return;
    }return structuredClone(disk);
  }};
  const state=await import('data:text/javascript;base64,'+Buffer.from(bundle.outputFiles[0].text+'\n//'+serial++).toString('base64'));
  return {state,entered,fail(){reject=true;},block(){block=true;},release(){release();},disk:()=>disk};
}
function edit(s,value){s.settings.value={...s.settings.value,byWidget:{dock:{...s.settings.value.byWidget.dock,delay:value}}};}
test('failed write never marks the draft saved',async()=>{
  const {state:s,fail}=await fixture();edit(s,150);fail();
  assert.equal(await s.saveSettings(),false);
  assert.equal(s.hasChanges.value,true);
  assert.equal(s.settings.value.byWidget.dock.delay,150);
});
test('native echo from an earlier write cannot overwrite a newer slider value',async()=>{
  const f=await fixture();const s=f.state;edit(s,200);f.block();
  const saving=s.saveSettings();await f.entered;edit(s,150);f.release();await saving;
  assert.equal(s.settings.value.byWidget.dock.delay,150);
  assert.equal(s.hasChanges.value,true);
});
test('external changes merge untouched fields without dropping local edits',async()=>{
  const {state:s}=await fixture();edit(s,150);
  globalThis.primaryFixture.event({payload:{...structuredClone(s.settings.value),language:'zh-CN',byWidget:{dock:{delay:800,enabled:false}}}});
  assert.equal(s.settings.value.byWidget.dock.delay,150);
  assert.equal(s.settings.value.byWidget.dock.enabled,false);
  assert.equal(s.settings.value.language,'zh-CN');
});
