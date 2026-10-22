import { test } from 'node:test';
import assert from 'node:assert/strict';
import { difference, isDirty } from '../src/ui/react/settings/modules/desktopSuite/preferencesDraft.ts';

test('loading preferences is clean and does not synthesize defaults or tracking values', () => {
  const data={Metrics:[],Unknown:{keep:true},ThreeFingerDeviceDragCursorConfigs:{device:{speed:182}}};
  assert.deepEqual(difference(data,structuredClone(data)),{});
  assert.equal(isDirty(data,structuredClone(data)),false);
});
test('only explicitly changed root settings enter the save patch', () => {
  const base={ThreeFingerDrag:true,FourFingerGestures:false,PointerBackupSpeed:10};
  assert.deepEqual(difference(base,{...base,ThreeFingerDrag:false}),{ThreeFingerDrag:false});
});
test('array order is significant, nested values remain immutable, reverting clears dirty', () => {
  const base={Metrics:[{Id:'cpu',IsEnabled:true},{Id:'gpu',IsEnabled:false}]};
  const draft=structuredClone(base);draft.Metrics.reverse();
  assert.equal(isDirty(base,draft),true);assert.equal(base.Metrics[0].Id,'cpu');
  draft.Metrics.reverse();assert.equal(isDirty(base,draft),false);
});
