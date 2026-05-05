export function createToolHotkeyListener(dotNetReference) {
    const handleKeyDown = async (event) => {
        const key = event.key || "";
        const editableTarget = isEditableTarget(event.target);

        if (event.defaultPrevented
            || key.length !== 1
            || editableTarget) {
            return;
        }

        const preemptBrowserShortcut = isBrowserUndoRedoShortcut(event);
        if (preemptBrowserShortcut) {
            event.preventDefault();
        }

        const handled = await dotNetReference.invokeMethodAsync(
            "OnToolHotkeyPressed",
            key,
            event.ctrlKey,
            event.altKey,
            event.shiftKey,
            event.metaKey,
            editableTarget);

        if (handled && !preemptBrowserShortcut) {
            event.preventDefault();
        }
    };

    window.addEventListener("keydown", handleKeyDown, true);

    return {
        dispose() {
            window.removeEventListener("keydown", handleKeyDown, true);
        }
    };
}

export function getStoredToolHotkeys(storageKey) {
    return window.localStorage.getItem(storageKey);
}

export function setStoredToolHotkeys(storageKey, value) {
    window.localStorage.setItem(storageKey, value);
}

export function clearStoredToolHotkeys(storageKey) {
    window.localStorage.removeItem(storageKey);
}

export function enableToolHotkeyRecorders(root) {
    const container = root || document;
    const inputs = container.querySelectorAll("[data-dxfer-hotkey-input='true']");

    for (const input of inputs) {
        if (input.dataset.dxferHotkeyRecorder === "true") {
            continue;
        }

        input.dataset.dxferHotkeyRecorder = "true";
        input.addEventListener("keydown", handleHotkeyRecorderKeyDown);
    }
}

function isEditableTarget(target) {
    if (!target || target === document || target === window) {
        return false;
    }

    if (target.isContentEditable) {
        return true;
    }

    const tagName = (target.tagName || "").toLowerCase();
    return tagName === "input"
        || tagName === "textarea"
        || tagName === "select"
        || target.closest("[contenteditable='true']") !== null;
}

function isBrowserUndoRedoShortcut(event) {
    return !event.altKey
        && (event.ctrlKey || event.metaKey)
        && String(event.key || "").toLowerCase() === "z";
}

function handleHotkeyRecorderKeyDown(event) {
    if (event.isComposing) {
        return;
    }

    if (event.key === "Tab") {
        return;
    }

    if (event.key === "Escape") {
        event.currentTarget.blur();
        return;
    }

    if (event.key === "Backspace" || event.key === "Delete") {
        updateHotkeyInput(event.currentTarget, "");
        event.preventDefault();
        event.stopPropagation();
        return;
    }

    const baseKey = normalizeRecordedBaseKey(event.key);
    if (!baseKey) {
        return;
    }

    const parts = [];
    if (event.ctrlKey) {
        parts.push("Ctrl");
    }

    if (event.altKey) {
        parts.push("Alt");
    }

    if (event.shiftKey) {
        parts.push("Shift");
    }

    if (event.metaKey) {
        parts.push("Meta");
    }

    parts.push(baseKey);
    updateHotkeyInput(event.currentTarget, parts.join("+"));
    event.preventDefault();
    event.stopPropagation();
}

function normalizeRecordedBaseKey(key) {
    if (!key || key.length !== 1 || !/^[a-z0-9]$/i.test(key)) {
        return null;
    }

    return key.toUpperCase();
}

function updateHotkeyInput(input, value) {
    input.value = value;
    input.dispatchEvent(new Event("change", { bubbles: true }));
}
