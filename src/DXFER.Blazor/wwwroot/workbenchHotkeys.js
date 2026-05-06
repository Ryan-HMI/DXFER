export function createToolHotkeyListener(dotNetReference) {
    const handleKeyDown = async (event) => {
        const key = getToolHotkeyEventKey(event);
        const editableTarget = isEditableTarget(event.target);

        if (event.defaultPrevented
            || !key
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

    if (event.key === "Backspace") {
        updateHotkeyInput(event.currentTarget, "");
        event.preventDefault();
        event.stopPropagation();
        return;
    }

    const baseKey = getToolHotkeyEventKey(event);
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

export function getToolHotkeyEventKey(event) {
    if (!event || event.isComposing) {
        return null;
    }

    return normalizeRecordedBaseKey(event.key || "");
}

export function normalizeRecordedBaseKey(key) {
    if (!key) {
        return null;
    }

    if (key.length === 1) {
        if (/^[a-z0-9]$/i.test(key)) {
            return key.toUpperCase();
        }

        return punctuationKeyNames.get(key) || null;
    }

    if (isModifierOnlyKey(key) || isInvalidBrowserKey(key)) {
        return null;
    }

    const functionMatch = /^f([1-9]|1[0-9]|2[0-4])$/i.exec(key);
    if (functionMatch) {
        return `F${functionMatch[1]}`;
    }

    return namedKeyNames.get(key.toLowerCase()) || key.toUpperCase();
}

function updateHotkeyInput(input, value) {
    input.value = value;
    input.dispatchEvent(new Event("change", { bubbles: true }));
}

const punctuationKeyNames = new Map([
    [" ", "Space"],
    ["+", "Plus"],
    ["-", "Minus"],
    ["=", "Equal"],
    [",", "Comma"],
    [".", "Period"],
    ["/", "Slash"],
    ["\\", "Backslash"],
    [";", "Semicolon"],
    ["'", "Quote"],
    ["`", "Backquote"],
    ["[", "BracketLeft"],
    ["]", "BracketRight"]
]);

const namedKeyNames = new Map([
    ["backspace", "Backspace"],
    ["delete", "Delete"],
    ["del", "Delete"],
    ["insert", "Insert"],
    ["ins", "Insert"],
    ["enter", "Enter"],
    ["return", "Enter"],
    ["escape", "Escape"],
    ["esc", "Escape"],
    ["tab", "Tab"],
    ["space", "Space"],
    ["spacebar", "Space"],
    ["home", "Home"],
    ["end", "End"],
    ["pageup", "PageUp"],
    ["pgup", "PageUp"],
    ["pagedown", "PageDown"],
    ["pgdn", "PageDown"],
    ["arrowleft", "ArrowLeft"],
    ["left", "ArrowLeft"],
    ["arrowright", "ArrowRight"],
    ["right", "ArrowRight"],
    ["arrowup", "ArrowUp"],
    ["up", "ArrowUp"],
    ["arrowdown", "ArrowDown"],
    ["down", "ArrowDown"],
    ["printscreen", "PrintScreen"],
    ["scrolllock", "ScrollLock"],
    ["pause", "Pause"],
    ["capslock", "CapsLock"],
    ["contextmenu", "ContextMenu"],
    ["plus", "Plus"],
    ["add", "Plus"],
    ["minus", "Minus"],
    ["hyphen", "Minus"],
    ["dash", "Minus"],
    ["equal", "Equal"],
    ["equals", "Equal"],
    ["comma", "Comma"],
    ["period", "Period"],
    ["dot", "Period"],
    ["slash", "Slash"],
    ["forwardslash", "Slash"],
    ["backslash", "Backslash"],
    ["semicolon", "Semicolon"],
    ["quote", "Quote"],
    ["apostrophe", "Quote"],
    ["backquote", "Backquote"],
    ["grave", "Backquote"],
    ["bracketleft", "BracketLeft"],
    ["leftbracket", "BracketLeft"],
    ["bracketright", "BracketRight"],
    ["rightbracket", "BracketRight"]
]);

function isModifierOnlyKey(key) {
    return /^(control|ctrl|alt|shift|meta|cmd|command|win|os)$/i.test(key);
}

function isInvalidBrowserKey(key) {
    return /^(dead|unidentified|process)$/i.test(key)
        || /\s/.test(key)
        || key.includes("+");
}
