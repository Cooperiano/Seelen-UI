import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createAutoSaveQueue} from '../src/ui/react/settings/state/autoSaveQueue.ts';
import {rebase} from '../src/ui/react/settings/state/settingsMerge.ts';
const pause=()=>new Promise(r=>setTimeout(r,30));
function fixture(save){let draft='',saved='';const states=[];const writes=[];
  const q=createAutoSaveQueue({key:()=>draft===saved?'':draft,status:s=>states.push(s),delay:5,
    async save(){const value=draft;writes.push(value);if(save)await save(value);saved=value;return true;}});
  return {q,writes,states,edit(v){draft=v;q.changed(draft===saved?'':draft);}};
}
test('load does not write and a slider burst saves only its final value automatically',async()=>{
  const f=fixture();await pause();assert.deepEqual(f.writes,[]);
  f.edit('1');f.edit('2');f.edit('3');await pause();
  assert.deepEqual(f.writes,['3']);assert.equal(f.states.at(-1),'saved');
});
test('edits during an in-flight write are serialized and retained',async()=>{
  let release;let entered;const started=new Promise(r=>entered=r);
  const f=fixture(async v=>{if(v==='1'){entered();await new Promise(r=>release=r);}});
  f.edit('1');const flushing=f.q.flush();await started;f.edit('2');f.edit('3');release();
  assert.equal(await flushing,true);assert.deepEqual(f.writes,['1','3']);
});
test('close flush does not wait for debounce',async()=>{const f=fixture();f.edit('1');assert.equal(await f.q.flush(),true);assert.deepEqual(f.writes,['1']);});
test('duplicate observations, reverted drafts and repeated close requests do not duplicate writes',async()=>{
  const f=fixture();assert.equal(await f.q.flush(),true);f.edit('1');f.edit('1');f.edit('');await pause();assert.deepEqual(f.writes,[]);
  let release;const q=fixture(async()=>new Promise(r=>release=r));q.edit('a');const first=q.q.flush();const second=q.q.flush();
  assert.equal(first,second);release();await first;assert.deepEqual(q.writes,['a']);
});
test('failure is visible, retains work and never retries in a loop; retry succeeds',async()=>{
  let reject=true;const f=fixture(async()=>{if(reject)throw Error('disk full');});
  f.edit('1');await pause();assert.equal(f.states.at(-1),'error');await pause();assert.deepEqual(f.writes,['1']);
  reject=false;assert.equal(await f.q.flush(),true);assert.deepEqual(f.writes,['1','1']);
});
test('an edit after failure retries automatically and acknowledgement is required',async()=>{
  let reject=true;const f=fixture(async()=>{if(reject)throw Error();});f.edit('1');await pause();reject=false;f.edit('2');await pause();assert.deepEqual(f.writes,['1','2']);
  const states=[];const q=createAutoSaveQueue({key:()=> 'dirty',save:async()=>true,status:s=>states.push(s)});
  assert.equal(await q.flush(),false);assert.equal(states.at(-1),'error');
});
test('rebase preserves deletions and treats changed arrays atomically',()=>{
  assert.deepEqual(rebase({a:1,b:2,list:[1]},{a:1,list:[2]},{a:3,b:2,list:[3],extra:true}),{a:3,list:[2],extra:true});
});
