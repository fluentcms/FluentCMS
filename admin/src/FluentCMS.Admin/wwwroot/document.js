// The admin's interop with the document: its language and direction, the branding's accent as a custom property, and the
// return of focus to a dialog's opener, and focus on the page's heading.

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

export function focusHeading() {
    const heading = document.querySelector("main h1");
    if (heading instanceof HTMLElement) {
        heading.focus();
    }
}
