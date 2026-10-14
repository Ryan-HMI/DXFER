export const storageKey = 'dxfer.paletteLayout.v1';
const maxGroups = 128;
const maxLength = 65536;

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
    return { groupName, side, compaction, dockOrder, floatingX, floatingY };
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
                states.set(state.groupName, copyState(state));
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
        const json = JSON.stringify({ version: 1, groups: [...stored.values()] });
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

export function observeLayout(root, host = window) {
    const refresh = () => {
        const bounds = root.getBoundingClientRect();
        const width = Math.max(0, Math.min(bounds.width, host.innerWidth - bounds.left));
        const height = Math.max(0, Math.min(bounds.height, host.innerHeight - bounds.top));
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
            observer.disconnect();
            host.removeEventListener('resize', refresh);
        }
    };
}
