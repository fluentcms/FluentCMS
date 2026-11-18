// The media library's drop and paste (spec 4.14): files dropped anywhere on the library, or pasted while it is open, are
// handed to the file input of its upload, whose change Blazor reads as if they had been chosen. No file leaves the
// browser here, and the access token stays in C#.

function hasFiles(event) {
    return event.dataTransfer?.types.includes("Files") === true;
}

function uploadInput(zone) {
    return zone.querySelector("[data-media-upload] input[type=file]");
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

// The zone takes the drops; the input, found when files come since the upload renders again, fills itself on a drop onto it.
export function listen(zone, activeClass) {
    let depth = 0;
    const over = event => {
        if (hasFiles(event)) {
            event.preventDefault();
            event.dataTransfer.dropEffect = "copy";
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
        const input = uploadInput(zone);
        if (input && event.target !== input) {
            event.preventDefault();
            give(input, event.dataTransfer.files);
        } else if (!input) {
            event.preventDefault();
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

    zone.addEventListener("dragover", over);
    zone.addEventListener("dragenter", enter);
    zone.addEventListener("dragleave", leave);
    zone.addEventListener("drop", drop);
    document.addEventListener("paste", paste);
    return {
        dispose() {
            zone.removeEventListener("dragover", over);
            zone.removeEventListener("dragenter", enter);
            zone.removeEventListener("dragleave", leave);
            zone.removeEventListener("drop", drop);
            document.removeEventListener("paste", paste);
        },
    };
}
