export function createToolHotkeyListener(dotNetReference) {
    const handleKeyDown = async (event) => {
        const key = event.key || "";
        const editableTarget = isEditableTarget(event.target);

        if (event.defaultPrevented
            || key.length !== 1
            || editableTarget) {
            return;
        }

        const handled = await dotNetReference.invokeMethodAsync(
            "OnToolHotkeyPressed",
            key,
            event.ctrlKey,
            event.altKey,
            event.shiftKey,
            event.metaKey,
            editableTarget);

        if (handled) {
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
