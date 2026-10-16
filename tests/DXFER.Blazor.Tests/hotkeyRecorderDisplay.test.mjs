import test from 'node:test';
import assert from 'node:assert/strict';
import {enableToolHotkeyRecorders} from '../../src/DXFER.Blazor/wwwroot/workbenchHotkeys.js';

test('rejected shortcut restores accepted display without duplicate recorder listeners',()=>{
  const input={dataset:{dxferAcceptedHotkey:'C'},value:'L',listeners:0,addEventListener(){this.listeners++;}};
  const root={querySelectorAll:()=>[input]};
  enableToolHotkeyRecorders(root);assert.equal(input.value,'C');assert.equal(input.listeners,1);
  input.value='L';enableToolHotkeyRecorders(root);assert.equal(input.value,'C');assert.equal(input.listeners,1);
  input.dataset.dxferAcceptedHotkey='';enableToolHotkeyRecorders(root);assert.equal(input.value,'');
});

test('recorders without accepted-value metadata retain their existing value',()=>{
  const input={dataset:{},value:'Shift+A',addEventListener(){}};
  enableToolHotkeyRecorders({querySelectorAll:()=>[input]});assert.equal(input.value,'Shift+A');
});
