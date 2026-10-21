// The existing pattern editor owns validation, commit and history.
export function patternViewportGuides(data) {
  const values = ['sourceX', 'sourceY', 'pitchX', 'pitchY', 'angle', 'centerX', 'centerY', 'step'];
  const n = Object.fromEntries(values.map(key => [key, Number(data[key])]));
  if (Object.values(n).some(value => !Number.isFinite(value))) return [];
  const source = { x: n.sourceX, y: n.sourceY };
  if (data.patternKind === 'Radial') {
    const center = { x: n.centerX, y: n.centerY }, angle = n.step * Math.PI / 180;
    const x = source.x - center.x, y = source.y - center.y;
    return [{ start: center, end: source }, { start: center,
      end: { x: center.x + x * Math.cos(angle) - y * Math.sin(angle), y: center.y + x * Math.sin(angle) + y * Math.cos(angle) } }];
  }
  const angle = n.angle * Math.PI / 180;
  return [{ start: source, end: { x: source.x + n.pitchX * Math.cos(angle), y: source.y + n.pitchX * Math.sin(angle) } },
    { start: source, end: { x: source.x - n.pitchY * Math.sin(angle), y: source.y + n.pitchY * Math.cos(angle) } }];
}

export function patternViewportAnchors(data) {
  const guides = patternViewportGuides(data);
  if (!guides.length) return null;
  const midpoint = g => ({x: (g.start.x + g.end.x) / 2, y: (g.start.y + g.end.y) / 2});
  const source = {x: Number(data.sourceX), y: Number(data.sourceY)};
  if (data.patternKind !== 'Radial') return {source, actions: source, x: midpoint(guides[0]), y: midpoint(guides[1])};
  const center = guides[0].start, angle = Number(data.step) * Math.PI / 360;
  const dx = source.x - center.x, dy = source.y - center.y;
  return {source, center, 'center-handle': center, actions: source,
    angle: {x: center.x + .65 * (dx * Math.cos(angle) - dy * Math.sin(angle)),
      y: center.y + .65 * (dx * Math.sin(angle) + dy * Math.cos(angle))}};
}

// Serialize server previews and coalesce movement; never allow an old preview
// to overtake the final release/cancel or flood the Blazor circuit.
export function createPatternCenterQueue(send, onIdle, onError) {
  let running = false, disposed = false;
  const queue = [];
  const pump = async () => {
    if (running || disposed) return;
    running = true;
    try {
      while (queue.length && !disposed) await send(queue.shift());
    } catch (error) {
      queue.length = 0;
      if (!disposed) onError(error);
    } finally {
      running = false;
      if (!disposed) onIdle();
    }
  };
  return {
    push(value) {
      if (disposed) return;
      if (value.phase === 'cancel') queue.splice(0, queue.length, ...queue.filter(x => x.phase === 'begin'));
      if (value.phase === 'update' && queue.at(-1)?.phase === 'update') queue[queue.length - 1] = value;
      else queue.push(value);
      void pump();
    },
    get busy() { return running || queue.length > 0; },
    dispose() { disposed = true; queue.length = 0; }
  };
}

export function bindPatternCenterHandle(widget, {toWorld, send, preview, onError}) {
  let drag = null;
  const queue = createPatternCenterQueue(send, () => {
    if (!drag) { delete widget.dataset.centerDragging; preview(null); }
  }, onError);
  const prevent = e => { e.preventDefault(); e.stopPropagation(); };
  const value = (phase, point) => ({id: widget.dataset.patternId, x: point.x, y: point.y, phase});
  const dragPoint = e => {
    const p = toWorld(e);
    return {x: p.x - drag.offset.x, y: p.y - drag.offset.y};
  };
  const down = e => {
    const handle = e.target.closest?.('[data-pattern-center-handle]');
    if (!handle || handle.disabled || e.button !== 0 || drag || queue.busy) return;
    const center = {x: Number(widget.dataset.centerX), y: Number(widget.dataset.centerY)}, p = toWorld(e);
    if (![center.x, center.y, p.x, p.y].every(Number.isFinite)) return;
    prevent(e);
    drag = {pointerId: e.pointerId, center, offset: {x: p.x - center.x, y: p.y - center.y}};
    widget.dataset.centerDragging = 'true';
    widget.setPointerCapture(e.pointerId);
    queue.push(value('begin', center));
  };
  const move = e => {
    if (!drag || drag.pointerId !== e.pointerId) return;
    prevent(e);
    const point = dragPoint(e);
    preview(point);
    queue.push(value('update', point));
  };
  const finish = (e, cancel) => {
    if (!drag || (e.pointerId !== undefined && drag.pointerId !== e.pointerId)) return;
    prevent(e);
    const point = cancel ? drag.center : dragPoint(e), pointerId = drag.pointerId;
    drag = null;
    if (widget.hasPointerCapture(pointerId)) widget.releasePointerCapture(pointerId);
    preview(point);
    queue.push(value(cancel ? 'cancel' : 'end', point));
  };
  const up = e => finish(e, false), cancel = e => finish(e, true);
  const key = e => { if (e.key === 'Escape' && drag) finish(e, true); };
  widget.addEventListener('pointerdown', down);
  widget.addEventListener('pointermove', move);
  widget.addEventListener('pointerup', up);
  widget.addEventListener('pointercancel', cancel);
  widget.addEventListener('lostpointercapture', cancel);
  widget.addEventListener('keydown', key);
  return () => {
    drag = null; queue.dispose();
    for (const [name, fn] of [['pointerdown',down],['pointermove',move],['pointerup',up],
      ['pointercancel',cancel],['lostpointercapture',cancel],['keydown',key]]) widget.removeEventListener(name, fn);
  };
}
