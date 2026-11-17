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

// The shortcuts of a form (spec 4.5): Ctrl or Cmd with S saves and with Shift and Enter publishes, and ? lists them where it
// types no character. A key the page handled already, such as Escape in a dialog, is left alone.
export function listenForShortcuts(target) {
    const listener = event => {
        if (event.defaultPrevented || event.isComposing || event.altKey || typeof event.key !== "string") {
            return;
        }

        const command = event.ctrlKey || event.metaKey
            ? (!event.shiftKey && event.key.toLowerCase() === "s" ? "save" : event.shiftKey && event.key === "Enter" ? "publish" : null)
            : event.key === "?" && !isTextEntry(event.composedPath()[0] ?? event.target) ? "help" : null;
        if (command === null) {
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
