// The admin's interop with the document: its language and direction, and the return of focus to a dialog's opener.

export function setLanguage(lang, dir) {
    document.documentElement.lang = lang;
    document.documentElement.dir = dir;
}

export function rememberFocus() {
    const opener = document.activeElement;
    return {
        restore() {
            if (opener instanceof HTMLElement && opener.isConnected) {
                opener.focus();
            }
        }
    };
}
