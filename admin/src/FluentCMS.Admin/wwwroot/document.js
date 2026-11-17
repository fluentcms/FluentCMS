// The admin's interop with the document: its language and direction, the branding's accent as a custom property, the
// return of focus to a dialog's opener, focus on the page's heading, the question before a tab with unsaved forms closes,
// and the keyboard shortcuts of a form.

export function setLanguage(lang, dir) {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;
}

export function setProperty(name, value) {
    document.documentElement.style.setProperty(name, value);
}

// Wrapped, since no element may have focus.
export function rememberFocus() {
    return { element: document.activeElement };
}

export function returnFocus(opener) {
    const element = opener.element;
    if (element instanceof HTMLElement && element.isConnected) {
        element.focus();
    }
}

// A delay lets a timer that gives focus back to a dialog's opener run first; timers of the page run in the order they expire.
export function focusHeading(delay) {
    setTimeout(() => {
        const heading = document.querySelector("main h1");
        if (heading instanceof HTMLElement) {
            heading.focus();
        }
    }, delay);
}

// The browser asks before the tab closes or reloads while a form has unsaved changes; the admin's own redirects turn it off
// first, once the forms are kept or given up.
let unloadGuarded = false;

function askBeforeUnload(event) {
    event.preventDefault();
    event.returnValue = "";
}

export function guardUnload(on) {
    if (on === unloadGuarded) {
        return;
    }

    unloadGuarded = on;
    if (on) {
        window.addEventListener("beforeunload", askBeforeUnload);
    } else {
        window.removeEventListener("beforeunload", askBeforeUnload);
    }
}

function isTextEntry(element) {
    return element instanceof HTMLElement
        && (element.isContentEditable || element.closest("input, textarea, select, [contenteditable='true'], [role='textbox']") !== null);
}

function isDialog(node) {
    return node instanceof HTMLDialogElement || (node instanceof HTMLElement && node.localName === "fluent-dialog");
}

// A modal dialog keeps focus inside it, so a key pressed while one is open comes from it; the library's dialog holds a
// native one in its shadow root, which is open while it shows.
function inOpenDialog(event) {
    return event.composedPath().some(isDialog)
        || document.querySelector("dialog[open]") !== null
        || [...document.querySelectorAll("fluent-dialog")].some(host => host.shadowRoot?.querySelector("dialog[open]"));
}

// The letter of a shortcut by its key, or by its place on the keyboard for a layout whose key gives another letter.
function isLetter(event, letter) {
    return event.key.toLowerCase() === letter || event.code === `Key${letter.toUpperCase()}`;
}

// The shortcuts of a form (spec 4.5): Ctrl or Cmd with S saves and with Shift and Enter publishes, and ? lists them where it
// types no character. A key the page handled already, and any key while a dialog is open, are left alone.
export function listenForShortcuts(target) {
    const listener = event => {
        if (event.defaultPrevented || event.isComposing || event.altKey || typeof event.key !== "string") {
            return;
        }

        const command = event.ctrlKey || event.metaKey
            ? (!event.shiftKey && isLetter(event, "s") ? "save" : event.shiftKey && event.key === "Enter" ? "publish" : null)
            : event.key === "?" && !isTextEntry(event.composedPath()[0] ?? event.target) ? "help" : null;
        if (command === null) {
            return;
        }

        // Saving the page is not what Ctrl+S means here, in a dialog either.
        if (inOpenDialog(event)) {
            if (command === "save") {
                event.preventDefault();
            }

            return;
        }

        event.preventDefault();
        target.invokeMethodAsync("RunAsync", command);
    };

    document.addEventListener("keydown", listener);
    return {
        dispose: () => document.removeEventListener("keydown", listener),
    };
}
