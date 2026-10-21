import assert from "node:assert/strict";
import test from "node:test";
import { createDrawingCanvas } from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

function harness(t) {
  const previousWindow = globalThis.window, previousDocument = globalThis.document;
  const calls = [], inputs = [];
  const element = () => {
    const handlers = new Map();
    return {
      dataset: {}, style: {}, classList: { add() {}, remove() {} },
      addEventListener: (name, handler) => handlers.set(name, handler), removeEventListener() {},
      setAttribute() {}, remove() {}, select() {},
      emit(name, event = {}) { handlers.get(name)?.({ preventDefault() {}, stopPropagation() {}, ...event }); },
      focus() {
        if (globalThis.document.activeElement === this) return;
        const previous = globalThis.document.activeElement;
        globalThis.document.activeElement = this;
        previous?.emit("blur");
        this.emit("focus");
      }
    };
  };
  globalThis.window = { devicePixelRatio: 1, addEventListener() {}, removeEventListener() {} };
  globalThis.document = { activeElement: null, createElement: element };
  const context = new Proxy({ createRadialGradient: () => ({ addColorStop() {} }),
    measureText: text => ({ width: String(text).length * 7 }) }, { get: (o, k) => k in o ? o[k] : () => {} });
  const canvas = { ...element(), clientWidth: 1000, clientHeight: 800, getContext: () => context,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 800 }) };
  const instance = createDrawingCanvas(canvas, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } },
    { appendChild: input => inputs.push(input) });
  t.after(() => {
    instance.dispose();
    if (previousWindow === undefined) delete globalThis.window; else globalThis.window = previousWindow;
    if (previousDocument === undefined) delete globalThis.document; else globalThis.document = previousDocument;
  });
  instance.setDocument({ entities: [], constraints: [], dimensions: [{ id: "gap", kind: "Offset", value: 1.000000323445391,
    referenceKeys: ["source", "dependent"], anchor: { x: 2, y: 2 }, isDriving: true,
    offsetStart: { x: 3, y: 0 }, offsetEnd: { x: 4.000000323445391, y: 0 } }] });
  return { input: inputs[0], canvas, instance, changes: () => calls.filter(call => call[0] === "OnSketchDimensionValueChanged") };
}

test("dimension Enter marks only the programmatic focus return, not later keyboard entry", t => {
  const h = harness(t);
  h.input.focus();
  h.input.emit("keydown", { key: "Enter" });
  assert.equal(document.activeElement, h.canvas);
  assert.equal(h.canvas.dataset.programmaticFocus, "true");
  h.input.focus();
  assert.equal(h.canvas.dataset.programmaticFocus, undefined);
  h.canvas.focus();
  assert.equal(h.canvas.dataset.programmaticFocus, undefined);
  assert.deepEqual(h.changes(), []);
});

test("component canvas focus uses the same marker and clears it on blur", t => {
  const h = harness(t);
  h.instance.focus();
  assert.equal(document.activeElement, h.canvas);
  assert.equal(h.canvas.dataset.programmaticFocus, "true");
  h.input.focus();
  assert.equal(h.canvas.dataset.programmaticFocus, undefined);
});

for (const action of ["blur", "Enter", "Escape"]) {
  test(`unedited rounded persistent input does not change geometry on ${action}`, t => {
    const h = harness(t);
    assert.equal(h.input.value, "1");
    h.input.focus();
    if (action === "blur") h.canvas.focus();
    else h.input.emit("keydown", { key: action });
    assert.deepEqual(h.changes(), []);
    assert.equal(h.input.dataset.dimensionValue, "1.000000323445391");
  });
}

for (const action of ["blur", "Enter", "change"]) {
  test(`explicitly typing the displayed rounded value still commits on ${action}`, t => {
    const h = harness(t);
    h.input.focus();
    h.input.value = "1";
    h.input.emit("input");
    if (action === "blur") h.canvas.focus();
    else if (action === "change") { h.input.emit("change"); h.canvas.focus(); }
    else h.input.emit("keydown", { key: action });
    assert.deepEqual(h.changes(), [["OnSketchDimensionValueChanged", "gap", 1]]);
  });
}
