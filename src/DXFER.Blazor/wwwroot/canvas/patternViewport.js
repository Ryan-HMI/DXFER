// The existing pattern editor owns validation, commit and history.
export function patternSourceScreenBounds(data, view, padding = 8) {
  const keys = ['sourceMinX', 'sourceMinY', 'sourceMaxX', 'sourceMaxY'];
  if (keys.some(key => data[key] == null || data[key] === '' || !Number.isFinite(Number(data[key])))
    || !view || ![view.scale, view.offsetX, view.offsetY, padding].every(Number.isFinite)
    || view.scale <= 0 || padding < 0) return null;
  const [minX, minY, maxX, maxY] = keys.map(key => Number(data[key]));
  if (maxX < minX || maxY < minY) return null;
  return {x: minX * view.scale + view.offsetX - padding,
    y: -maxY * view.scale + view.offsetY - padding,
    width: (maxX - minX) * view.scale + 2 * padding,
    height: (maxY - minY) * view.scale + 2 * padding};
}

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
  const explicit = ['uX', 'uY', 'vX', 'vY'].every(key => data[key] !== undefined && Number.isFinite(Number(data[key])));
  const u = explicit ? {x: Number(data.uX), y: Number(data.uY)} : {x: Math.cos(angle), y: Math.sin(angle)};
  const v = explicit ? {x: Number(data.vX), y: Number(data.vY)} : {x: -Math.sin(angle), y: Math.cos(angle)};
  return [{ start: source, end: { x: source.x + n.pitchX * u.x, y: source.y + n.pitchX * u.y } },
    { start: source, end: { x: source.x + n.pitchY * v.x, y: source.y + n.pitchY * v.y } }];
}

export function patternViewportAnchors(data) {
  const guides = patternViewportGuides(data);
  if (!guides.length) return null;
  const midpoint = g => ({x: (g.start.x + g.end.x) / 2, y: (g.start.y + g.end.y) / 2});
  const source = {x: Number(data.sourceX), y: Number(data.sourceY)};
  if (data.patternKind !== 'Radial') return {source, actions: source, x: midpoint(guides[0]), y: midpoint(guides[1]),
    stagger: {x: guides[0].end.x + guides[1].end.x - source.x, y: guides[0].end.y + guides[1].end.y - source.y}};
  const center = guides[0].start, angle = Number(data.step) * Math.PI / 360;
  const dx = source.x - center.x, dy = source.y - center.y;
  return {source, center, 'center-handle': center, actions: source,
    angle: {x: center.x + .65 * (dx * Math.cos(angle) - dy * Math.sin(angle)),
      y: center.y + .65 * (dx * Math.sin(angle) + dy * Math.cos(angle))}};
}

const finitePoint = p => p && Number.isFinite(p.x) && Number.isFinite(p.y);
const samePoint = (a, b) => finitePoint(a) && finitePoint(b) && a.x === b.x && a.y === b.y;
const pointArrays = ['points', 'controlPoints', 'fitPoints'];
const pointFields = ['center', 'startTangentHandle', 'endTangentHandle'];

// The server alone grants eligibility. Translation preserves radii, angles,
// spline weights and ellipse axis vectors; every frame uses the same baseline.
export function radialCenterTransform(document, id, center) {
  const descriptor = document?.radialCenterPreviews?.find(p => p.id === id);
  if (!descriptor || !samePoint(descriptor.center, center) || !Number.isFinite(descriptor.stepAngleDegrees)
    || !Array.isArray(descriptor.members) || !Array.isArray(document.entities)) return null;
  const moved = new Map();
  for (const member of descriptor.members) {
    if (!member.entityId || !Number.isInteger(member.column) || member.column < 1 || moved.has(member.entityId)) return null;
    const angle = member.column * descriptor.stepAngleDegrees * Math.PI / 180;
    if (!Number.isFinite(angle)) return null;
    moved.set(member.entityId, {cos: Math.cos(angle), sin: Math.sin(angle)});
  }
  if (descriptor.centerPointId != null) {
    if (moved.has(descriptor.centerPointId)) return null;
    const entity = document.entities.find(e => e.id === descriptor.centerPointId);
    if (entity?.kind !== 'point' || !samePoint(entity.points?.[0], center)) return null;
    moved.set(descriptor.centerPointId, null);
  }
  const entities = new Map(document.entities.map(e => [e.id, e]));
  if (entities.size !== document.entities.length) return null;
  for (const id of moved.keys()) {
    const entity = entities.get(id);
    if (!entity || entity.isLocked || !['point', 'line', 'polyline', 'circle', 'arc', 'ellipse', 'polygon', 'spline'].includes(entity.kind)
      || pointArrays.some(key => entity[key] != null && (!Array.isArray(entity[key]) || !entity[key].every(finitePoint)))
      || pointFields.some(key => entity[key] != null && !finitePoint(entity[key]))) return null;
  }
  const origin = {...center};
  return point => {
    if (!finitePoint(point)) return null;
    const dx = point.x - origin.x, dy = point.y - origin.y;
    if (!Number.isFinite(dx) || !Number.isFinite(dy)) return null;
    let valid = true;
    const result = document.entities.map(entity => {
      if (!moved.has(entity.id)) return entity;
      const rotation = moved.get(entity.id);
      if (rotation === null) return {...entity, points: [{...entity.points[0], ...point}]};
      const x = rotation ? dx - rotation.cos * dx + rotation.sin * dy : dx;
      const y = rotation ? dy - rotation.sin * dx - rotation.cos * dy : dy;
      const translate = p => {
        const translated = {...p, x: p.x + x, y: p.y + y};
        valid &&= Boolean(finitePoint(translated));
        return translated;
      };
      const copy = {...entity};
      for (const key of pointArrays) if (entity[key]) copy[key] = entity[key].map(translate);
      for (const key of pointFields) if (entity[key]) copy[key] = translate(entity[key]);
      return copy;
    });
    return valid ? {...document, entities: result} : null;
  };
}

export function createRadialCenterPreview({getDocument, render, requestFrame, cancelFrame}) {
  let session = null, frame = null;
  const cancelPendingFrame = () => { if (frame != null) cancelFrame(frame); frame = null; };
  const paint = () => {
    frame = null;
    if (session) {
      const document = session.transform(session.point);
      render(document ? session.point : null, document);
    }
  };
  const reset = () => { cancelPendingFrame(); session = null; render(null, null); };
  return {
    get busy() { return session != null; },
    begin(id, center) {
      if (session) return false;
      const transform = radialCenterTransform(getDocument(), id, center);
      if (!transform) return false;
      session = {id, transform, point: {...center}, held: true, accepted: null};
      return true;
    },
    move(point) {
      if (!session || !finitePoint(point)) return;
      session.point = {...point};
      if (frame == null) frame = requestFrame(paint);
    },
    finish(point) {
      if (!session) return;
      session.held = false;
      session.point = {...point};
      cancelPendingFrame();
      paint();
      if (samePoint(session?.accepted, point)) reset();
    },
    accept(document) {
      if (!session) return;
      const descriptor = document?.radialCenterPreviews?.find(p => p.id === session.id);
      if (!descriptor) { reset(); return; }
      session.accepted = descriptor.center;
      if (!session.held && samePoint(descriptor.center, session.point)) reset();
    },
    reset
  };
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

export function bindPatternCenterHandle(widget, {toWorld, send, preview, select, onError,
  beginLocal, finishLocal, resetLocal, localBusy}) {
  let drag = null, lastSent = null;
  const queue = createPatternCenterQueue(value => { lastSent = value; return send(value); }, () => {
    if (!drag) { delete widget.dataset.centerDragging; preview(null); }
  }, error => {
    const failed = drag;
    drag = null;
    if (failed && widget.hasPointerCapture(failed.pointerId)) widget.releasePointerCapture(failed.pointerId);
    delete widget.dataset.centerDragging;
    resetLocal?.(); preview(null); onError(error);
    // A failed response may have reached the server. Best-effort cancellation
    // releases its staged drag guard; never retry updates or an end implicitly.
    if (lastSent && lastSent.phase !== 'cancel') {
      void Promise.resolve().then(() => send({...lastSent, phase: 'cancel'})).catch(onError);
    }
  });
  const prevent = e => { e.preventDefault(); e.stopPropagation(); };
  const value = (phase, point, id = drag?.id) => ({id, x: point.x, y: point.y, phase});
  const dragPoint = e => {
    const p = toWorld(e);
    return {x: p.x - drag.offset.x, y: p.y - drag.offset.y};
  };
  const down = e => {
    const handle = e.target.closest?.('[data-pattern-center-handle]');
    if (!handle || handle.disabled || e.button !== 0 || drag || queue.busy || localBusy?.()) return;
    const center = {x: Number(widget.dataset.centerX), y: Number(widget.dataset.centerY)}, p = toWorld(e);
    if (![center.x, center.y, p.x, p.y].every(Number.isFinite)) return;
    prevent(e);
    drag = {id: widget.dataset.patternId, pointerId: e.pointerId, center, screen: {x: e.clientX, y: e.clientY}, started: false,
      offset: {x: p.x - center.x, y: p.y - center.y}};
    widget.setPointerCapture(e.pointerId);
  };
  const start = e => {
    if (drag.started) return true;
    if (Math.hypot(e.clientX - drag.screen.x, e.clientY - drag.screen.y) < 3) return false;
    drag.started = true;
    drag.local = Boolean(beginLocal?.(drag.id, drag.center));
    widget.dataset.centerDragging = 'true';
    queue.push(value('begin', drag.center));
    return true;
  };
  const move = e => {
    if (!drag || drag.pointerId !== e.pointerId) return;
    prevent(e);
    if (!start(e)) return;
    const point = dragPoint(e);
    if (!finitePoint(point)) return;
    preview(point);
    if (!drag.local) queue.push(value('update', point));
  };
  const finish = (e, cancel) => {
    if (!drag || (e.pointerId !== undefined && drag.pointerId !== e.pointerId)) return;
    prevent(e);
    if (!cancel) start(e);
    let point = cancel ? drag.center : dragPoint(e);
    if (!finitePoint(point)) { cancel = true; point = drag.center; }
    const {pointerId, started, id, local} = drag;
    drag = null;
    if (widget.hasPointerCapture(pointerId)) widget.releasePointerCapture(pointerId);
    if (!started) {
      if (!cancel) select?.(Boolean(e.shiftKey));
      return;
    }
    if (local) finishLocal?.(point);
    else preview(point);
    queue.push(value(cancel ? 'cancel' : 'end', point, id));
  };
  const up = e => finish(e, false), cancel = e => finish(e, true);
  const key = e => {
    if (e.key === 'Escape' && drag) finish(e, true);
    else if ((e.key === 'Enter' || e.key === ' ') && !drag && !queue.busy
      && e.target.closest?.('[data-pattern-center-handle]') && !e.target.disabled) {
      prevent(e); select?.(Boolean(e.shiftKey));
    }
  };
  widget.addEventListener('pointerdown', down);
  widget.addEventListener('pointermove', move);
  widget.addEventListener('pointerup', up);
  widget.addEventListener('pointercancel', cancel);
  widget.addEventListener('lostpointercapture', cancel);
  widget.addEventListener('keydown', key);
  return () => {
    const active = drag;
    drag = null; queue.dispose();
    if (active && widget.hasPointerCapture(active.pointerId)) widget.releasePointerCapture(active.pointerId);
    delete widget.dataset.centerDragging;
    resetLocal?.();
    for (const [name, fn] of [['pointerdown',down],['pointermove',move],['pointerup',up],
      ['pointercancel',cancel],['lostpointercapture',cancel],['keydown',key]]) widget.removeEventListener(name, fn);
  };
}
