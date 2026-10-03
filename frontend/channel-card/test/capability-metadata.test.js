import test from 'node:test';
import assert from 'node:assert/strict';
import { describeAdvanced } from '../src/advanced-controls.js';
import { validateMetadata } from '../src/session-gate.js';
import { ControlPanel } from '../src/control-panel.js';

test('assembled controls module loads and complete physical capabilities fit metadata', () => {
  assert.equal(typeof ControlPanel, 'function');
  const ids = ['mono','comp','comp_gainin','comp_ratio','comp_threshold','comp_attack','comp_release','comp_knee','comp_gainout','comp_makeup',
    'gate','gate_threshold','gate_damping','gate_bpsidechain','gate_attack','gate_hold','gate_release','denoiser','denoiser_threshold','eq_on','eq_ab'];
  for(let channel=0;channel<8;channel++)for(let cell=0;cell<6;cell++)for(const field of ['on','type','f','gain','q'])ids.push(`eq_channel_${channel}_cell_${cell}_${field}`);
  const controls = ids.map(suffix => {
    const spec = describeAdvanced(`strip_0_${suffix}`, 'strip:0');
    return { ...spec, kind: spec.domain, discovery_unique_id: `vm_${spec.id}` };
  });
  assert.equal(controls.length,261);
  const value={schema:2,engine:'potato',session_id:'qualified',sources:[{id:'strip:0',kind:'strip',index:0,label:'Mic',enabled:true,taps:['pre','post_mute'],controls}]};
  assert.ok(validateMetadata(value));
  delete controls[0].step;
  assert.equal(validateMetadata(value),null);
});
