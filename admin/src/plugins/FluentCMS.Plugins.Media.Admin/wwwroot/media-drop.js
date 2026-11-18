// The media library's drop and paste (spec 4.14): files dropped anywhere on the library, or pasted while it is open, are
// handed to the file input of its upload, whose change Blazor reads as if they had been chosen. No file leaves the
// browser here, and the access token stays in C#.

function hasFiles(event) {
    return event.dataTransfer?.types.includes("Files") === true;
}

function uploadInput(zone) {
    return zone.querySelector("[data-media-upload] input[type=file]");
}

// A file input takes a drop onto it itself.
function isFileInput(target) {
    return target instanceof HTMLInputElement && target.type === "file";
}

function give(input, files) {
    const transfer = new DataTransfer();
    for (const file of files) {
        transfer.items.add(file);
    }

    input.files = transfer.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
}

// A paste of text into a field stays the field's; a paste that holds files, such as a copied image, is an upload.
function isTextPaste(event) {
    const target = event.target;
    const editable = target instanceof HTMLElement && (target.isContentEditable || target.closest("input, textarea, select") !== null);
    return editable && event.clipboardData?.types.includes("text/plain") === true;
}

// The zone takes the drops; the input is found when files come, since the upload renders again. While the input is
// disabled, as when files of an earlier choice wait to be read, a drop or paste is not taken.
export function listen(zone, activeClass) {
    let depth = 0;
    const over = event => {
        if (hasFiles(event)) {
            event.preventDefault();
            event.dataTransfer.dropEffect = uploadInput(zone)?.disabled === false ? "copy" : "none";
        }
    };
    const enter = event => {
        if (hasFiles(event) && depth++ === 0) {
            zone.classList.add(activeClass);
        }
    };
    const leave = event => {
        if (hasFiles(event) && --depth === 0) {
            zone.classList.remove(activeClass);
        }
    };
    const drop = event => {
        if (!hasFiles(event)) {
            return;
        }

        depth = 0;
        zone.classList.remove(activeClass);
        if (isFileInput(event.target)) {
            return;
        }

        event.preventDefault();
        const input = uploadInput(zone);
        if (input && !input.disabled) {
            give(input, event.dataTransfer.files);
        }
    };
    const paste = event => {
        const files = event.clipboardData?.files;
        const input = uploadInput(zone);
        if (input && !input.disabled && files && files.length > 0 && !isTextPaste(event)) {
            event.preventDefault();
            give(input, files);
        }
    };

    // Elsewhere on the page, as the header or the sidebar, a dropped file would make the browser open it and leave the admin.
    const away = event => {
        if (hasFiles(event) && !zone.contains(event.target) && !isFileInput(event.target)) {
            event.preventDefault();
            if (event.type === "dragover") {
                event.dataTransfer.dropEffect = "none";
            }
        }
    };

    zone.addEventListener("dragover", over);
    zone.addEventListener("dragenter", enter);
    zone.addEventListener("dragleave", leave);
    zone.addEventListener("drop", drop);
    document.addEventListener("paste", paste);
    document.addEventListener("dragover", away);
    document.addEventListener("drop", away);
    return {
        dispose() {
            zone.removeEventListener("dragover", over);
            zone.removeEventListener("dragenter", enter);
            zone.removeEventListener("dragleave", leave);
            zone.removeEventListener("drop", drop);
            document.removeEventListener("paste", paste);
            document.removeEventListener("dragover", away);
            document.removeEventListener("drop", away);
        },
    };
}
