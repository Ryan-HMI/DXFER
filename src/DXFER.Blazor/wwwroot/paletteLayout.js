export const storageKey = 'dxfer.paletteLayout.v1';
const maxGroups = 128;
const maxLength = 65536;
const densityVersion = 2;
const dockingVersion = 1;

function migrateDock(state, version) {
    if (version === dockingVersion) return state;
    const index = state.groupName === 'Cleanup' ? 6 : state.groupName === 'Grain' ? 7 : -1;
    // Previous saves included untouched defaults. Preserve any evidence of a moved palette.
    if (index >= 0 && state.side === 1 && state.dockOrder === 60 + index * 100
        && state.floatingX === 24 + (index % 4) * 24 && state.floatingY === 24 + index * 18) {
        return {...state, side: 2};
    }
    return state;
}

function validState(state) {
    return state && typeof state.groupName === 'string'
        && state.groupName.length > 0 && state.groupName.length <= 128
        && Number.isInteger(state.side) && state.side >= 0 && state.side <= 4
        && Number.isInteger(state.compaction) && state.compaction >= 0 && state.compaction <= 2
        && [state.dockOrder, state.floatingX, state.floatingY]
            .every(value => Number.isFinite(value) && value >= 0 && value <= 1000000);
}

function copyState(state) {
    const { groupName, side, compaction, dockOrder, floatingX, floatingY } = state;
    const result = { groupName, side, compaction, dockOrder, floatingX, floatingY,
        showLabels: state.showLabels === true };
    if (Number.isFinite(state.labelWidth) && state.labelWidth >= 96 && state.labelWidth <= 480)
        result.labelWidth = state.labelWidth;
    return result;
}

function storedStates(json) {
    const states = new Map();
    try {
        if (typeof json !== 'string' || json.length > maxLength) return states;
        const data = JSON.parse(json);
        if (data?.version !== 1 || !Array.isArray(data.groups) || data.groups.length > maxGroups) return states;
        const seen = new Set();
        for (const state of data.groups) {
            if (!validState(state)) continue;
            if (seen.has(state.groupName)) {
                states.delete(state.groupName);
            } else {
                // Old releases saved default-on labels indistinguishably from explicit choices.
                states.set(state.groupName, copyState({...migrateDock(state, data.dockingVersion),
                    showLabels: data.densityVersion === densityVersion && state.showLabels === true}));
                seen.add(state.groupName);
            }
        }
    } catch { /* Browser preferences must never prevent the workspace from opening. */ }
    return states;
}

export function restoreLayout(json, defaults) {
    const stored = storedStates(json);
    return defaults.map(state => copyState(stored.get(state.groupName) ?? state));
}

export function loadLayout(defaults, storage) {
    try {
        return restoreLayout((storage ?? window.localStorage).getItem(storageKey), defaults);
    } catch {
        return defaults.map(copyState);
    }
}

export function saveLayout(groups, storage) {
    try {
        if (!Array.isArray(groups) || groups.length > maxGroups || !groups.every(validState)) return false;
        const target = storage ?? window.localStorage;
        const stored = storedStates(target.getItem(storageKey));
        for (const state of groups) stored.set(state.groupName, copyState(state));
        if (stored.size > maxGroups) return false;
        const json = JSON.stringify({ version: 1, densityVersion, dockingVersion, groups: [...stored.values()] });
        if (json.length > maxLength) return false;
        target.setItem(storageKey, json);
        return true;
    } catch {
        return false;
    }
}

export function floatingLimits(width, height, paletteWidth, headerHeight) {
    return { x: Math.max(0, width - paletteWidth), y: Math.max(0, height - headerHeight) };
}

export function getBounds(root) {
    const bounds = root.getBoundingClientRect();
    return { left: bounds.left, top: bounds.top, width: bounds.width, height: bounds.height,
        scaleX: root.offsetWidth > 0 ? bounds.width / root.offsetWidth : 1,
        scaleY: root.offsetHeight > 0 ? bounds.height / root.offsetHeight : 1 };
}

export function captureResize(root, index, pointerId) {
    const group = root.querySelector(`[data-palette-index="${index}"]`);
    const bounds = getBounds(root);
    root.setPointerCapture(pointerId);
    return { ...bounds, groupWidth: group.getBoundingClientRect().width / bounds.scaleX };
}

export function measureLabelWidths(root) {
    const widths = {};
    for (const group of root.querySelectorAll('.dxfer-tool-group-labeled')) {
        // Measure intrinsic single-line content in the same scoped styles, without affecting layout.
        const clone = group.cloneNode(true);
        Object.assign(clone.style, {position:'absolute', visibility:'hidden', width:'max-content',
            minWidth:'0', maxWidth:'none', left:'0', top:'0', pointerEvents:'none'});
        clone.style.setProperty('width', 'max-content', 'important');
        const strip = clone.querySelector('.dxfer-command-strip');
        if (strip) strip.style.gridTemplateColumns = 'max-content';
        for (const label of clone.querySelectorAll('.dxfer-icon-button-label')) label.style.whiteSpace = 'nowrap';
        for (const button of clone.querySelectorAll('.dxfer-icon-button')) button.style.width = 'max-content';
        group.parentElement.append(clone);
        widths[group.dataset.paletteIndex] = Math.max(96, Math.ceil(clone.offsetWidth + 8));
        clone.remove();
    }
    return widths;
}

export function observeLayout(root, host = window) {
    const resizeKey = event => {
        if (event.target.closest?.('.dxfer-palette-resize') &&
            ['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) event.preventDefault();
    };
    root.addEventListener?.('keydown', resizeKey);
    const refresh = () => {
        const bounds = root.getBoundingClientRect();
        // CSS limits and offset sizes use layout pixels; client bounds include display scaling.
        const scaleX = root.offsetWidth > 0 ? bounds.width / root.offsetWidth : 1;
        const scaleY = root.offsetHeight > 0 ? bounds.height / root.offsetHeight : 1;
        const width = Math.max(0, Math.min(bounds.width, host.innerWidth - bounds.left)) / (scaleX || 1);
        const height = Math.max(0, Math.min(bounds.height, host.innerHeight - bounds.top)) / (scaleY || 1);
        for (const group of root.querySelectorAll('.dxfer-tool-group-floating')) {
            const header = group.querySelector('.dxfer-tool-group-drag-header');
            const limits = floatingLimits(width, height, group.offsetWidth, header?.offsetHeight ?? 40);
            // Keep limits on the host: Blazor replaces a group's inline style during dragging.
            const index = group.dataset.paletteIndex;
            root.style.setProperty(`--dxfer-palette-max-x-${index}`, `${limits.x}px`);
            root.style.setProperty(`--dxfer-palette-max-y-${index}`, `${limits.y}px`);
        }
    };
    const observer = new host.ResizeObserver(refresh);
    observer.observe(root);
    host.addEventListener('resize', refresh);
    refresh();
    return {
        refresh,
        dispose() {
            root.removeEventListener?.('keydown', resizeKey);
            observer.disconnect();
            host.removeEventListener('resize', refresh);
        }
    };
}
