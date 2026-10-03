import test from 'node:test'; import assert from 'node:assert/strict';
import { PeakMotion } from '../src/peak-motion.js';
test('attack is immediate and silence continues elapsed-time decay; unavailable resets',()=>{
 const m=new PeakMotion(-90,5,1500);m.observe(-10,0);assert.equal(m.view(0).level,-10);
 m.observe(-30,50);assert.ok(m.view(100).level>-30);assert.equal(m.view(1000).level,-30);
 m.observe(-90,1001);assert.ok(m.view(1001).level>-90);assert.ok(m.view(1501).level>-90);assert.equal(m.view(4000).level,-90);m.reset();assert.equal(m.view(4100).level,null);
});
test('true hold uses selected readings and bounded history, reduced motion renders measured value',()=>{
 const m=new PeakMotion(-90,3,100);m.observe(-12,0);m.observe(-40,50);assert.equal(m.view(75,true).level,-40);assert.equal(m.view(75).hold,-12);
 for(let i=0;i<10000;i++)m.observe(-30,i*50);assert.ok(m.history.length<=256);assert.ok(m.history.every(p=>p.time>=499950-3000));
 assert.equal(m.view(500200).clipping,false);
});
