import test from 'node:test';
import assert from 'node:assert/strict';
import { describeAdvanced, resolveRegistryEntities } from '../src/advanced-controls.js';
import { normalizeControls, readControl, controlRequest } from '../src/control-model.js';
test('advanced controls cannot cross sources or fabricate virtual hardware processing',()=>{
 assert.equal(describeAdvanced('strip_0_comp_threshold','strip:5'),null);
 assert.equal(describeAdvanced('strip_5_comp_threshold','strip:5'),null);
 assert.equal(describeAdvanced('bus_0_gate','bus:0'),null);
 assert.equal(describeAdvanced('strip_0_eq_channel_8_cell_0_f','strip:0'),null);
 assert.deepEqual([describeAdvanced('strip_0_comp_threshold','strip:0').min,describeAdvanced('strip_0_comp_threshold','strip:0').max],[-40,-3]);
});
test('registry resolution uses stable unique IDs and rejects duplicates',()=>{
 const descriptors=[{id:'strip_0_comp_threshold',discovery_unique_id:'bridge_unique'}];
 assert.deepEqual(resolveRegistryEntities(descriptors,[{unique_id:'bridge_unique',platform:'mqtt',entity_id:'number.renamed'}]),{strip_0_comp_threshold:'number.renamed'});
 assert.deepEqual(resolveRegistryEntities(descriptors,[{unique_id:'bridge_unique',platform:'mqtt',entity_id:'number.first'},{unique_id:'bridge_unique',platform:'mqtt',entity_id:'number.second'}]),{});
});
test('advanced commands use real HA readback and vendor bounds',()=>{
 const config=normalizeControls({source:{id:'strip:0'},controls:{compressor:true},entities:{advanced:{strip_0_comp_threshold:'number.comp'}}});
 const binding=config.bindings.find(b=>b.key==='advanced:strip_0_comp_threshold'); assert.ok(binding);
 const h={callService:async()=>{},connection:{connected:true},services:{number:{set_value:{}}},states:{'number.comp':{state:'-18',attributes:{min:-40,max:-3,step:0.1,unit_of_measurement:'dB'}}}};
 const view=readControl(binding,h); assert.equal(view.available,true);
 assert.equal(controlRequest(view,-50),null);
 assert.deepEqual(controlRequest(view,-20),{domain:'number',service:'set_value',data:{entity_id:'number.comp',value:-20}});
 h.states['number.comp'].attributes.max=100; assert.equal(readControl(binding,h).available,false);
});
test('EQ filter types are named choices and cells carry channel/cell coordinates',()=>{
 const type=describeAdvanced('strip_0_eq_channel_2_cell_4_type','strip:0');
 assert.equal(type.input,'select');assert.equal(type.channel,2);assert.equal(type.cell,4);assert.equal(type.choices.length,7);
 assert.equal(type.choices[0].value,0);assert.ok(type.choices.every(choice=>choice.label && Number.isInteger(choice.value)));
 const frequency=describeAdvanced('bus_0_eq_channel_0_cell_0_f','bus:0');assert.equal(frequency.input,'frequency');
 assert.equal(describeAdvanced('strip_0_comp_attack','strip:0').input,'number');
});
