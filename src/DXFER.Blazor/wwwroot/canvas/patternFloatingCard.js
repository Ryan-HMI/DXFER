import { getBounds } from '../paletteLayout.js';
import { getUnobscuredViewport } from './viewportFit.js';

// Screen-space UI state only: no document, solver, interop or history dependency.
export function createPatternCardLayout(canvas) {
  let card = null, position = null, size = null, drag = null;
  const clamp = point => ({
    x: Math.max(8, Math.min(point.x, size.width - card.offsetWidth - 8)),
    y: Math.max(8, Math.min(point.y, size.height - card.offsetHeight - 8))
  });
  const paint = point => {
    position = clamp(point);
    card.style.left = `${position.x}px`;
    card.style.top = `${position.y}px`;
    card.style.visibility = 'visible';
    return {...position, width:card.offsetWidth, height:card.offsetHeight};
  };
  const isHeader = target => {
    const handle = target?.closest?.('[data-pattern-card-drag]');
    return handle && card?.contains(handle);
  };
  const stop = event => {event.preventDefault();event.stopPropagation();};
  const release = rollback => {
    const previous = drag;
    drag = null;
    if (!previous) return;
    if (rollback) paint(previous.position);
    if (card.hasPointerCapture(previous.id)) card.releasePointerCapture(previous.id);
  };
  const down = event => {
    if (drag || event.button !== 0 || !isHeader(event.target)) return;
    const bounds = getBounds(canvas);
    drag = {id:event.pointerId, x:event.clientX, y:event.clientY, position:{...position},
      scaleX:bounds.scaleX || 1, scaleY:bounds.scaleY || 1};
    event.target.closest('[data-pattern-card-drag]').focus?.({preventScroll:true});
    card.setPointerCapture(event.pointerId);
    stop(event);
  };
  const move = event => {
    if (!drag || event.pointerId !== drag.id) return;
    paint({x:drag.position.x + (event.clientX - drag.x) / drag.scaleX,
      y:drag.position.y + (event.clientY - drag.y) / drag.scaleY});
    stop(event);
  };
  const up = event => {
    if (!drag || event.pointerId !== drag.id) return;
    move(event);release(false);
  };
  const cancel = event => {
    if (!drag || event.pointerId !== drag.id) return;
    release(true);stop(event);
  };
  const key = event => {
    if (event.key === 'Escape' && drag) { release(true);stop(event);return; }
    if (!isHeader(event.target) || event.altKey || event.ctrlKey || event.metaKey) return;
    const delta = {ArrowLeft:[-1,0],ArrowRight:[1,0],ArrowUp:[0,-1],ArrowDown:[0,1]}[event.key];
    if (!delta) return;
    const step = event.shiftKey ? 1 : 8;
    paint({x:position.x + delta[0]*step,y:position.y + delta[1]*step});stop(event);
  };
  const listeners = {pointerdown:down,pointermove:move,pointerup:up,pointercancel:cancel,lostpointercapture:cancel,keydown:key};
  const detach = () => {
    release(true);
    if (card) for (const [name,handler] of Object.entries(listeners)) card.removeEventListener(name,handler);
    card = null;
  };
  return {
    place(widget, viewportSize, obstacles = []) {
      const next = widget?.querySelector('[data-pattern-card]') || null;
      if (card !== next) {
        detach();card = next;
        if (card) for (const [name,handler] of Object.entries(listeners)) card.addEventListener(name,handler);
      }
      size = viewportSize;
      if (!card) return null;
      if (!position) {
        const clear = getUnobscuredViewport(size, obstacles);
        const area = clear && clear.width >= card.offsetWidth + 16 && clear.height >= card.offsetHeight + 16
          ? clear : {x:0,y:0,...size};
        position = {x:Math.max(area.x + 8, Math.min(area.x + (area.width-card.offsetWidth)/2 + 32, area.x+area.width-card.offsetWidth-8)),
          y:Math.max(area.y + 8, Math.min(area.y + (area.height-card.offsetHeight)/2 + 24, area.y+area.height-card.offsetHeight-8))};
      }
      return paint(position);
    },
    dispose:detach
  };
}
