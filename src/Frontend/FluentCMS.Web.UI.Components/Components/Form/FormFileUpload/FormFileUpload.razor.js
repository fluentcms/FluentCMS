const fileUploads = new Map();

function onDragHover(event) {
    event.preventDefault();
}

function onDragLeave(event) {
    event.preventDefault();
}

function onDrop(event) {
    event.preventDefault();

    const input = event.currentTarget.querySelector('input');

    input.files = event.dataTransfer.files;

    const evt = new Event('change', { bubbles: true });

    input.dispatchEvent(evt);
}

export function initialize(dotnet, element) {
    dispose(dotnet, element);

    const fileUpload = element;
 
    fileUpload.addEventListener("dragenter", onDragHover);
    fileUpload.addEventListener("dragover", onDragHover);
    fileUpload.addEventListener("dragleave", onDragLeave);
    fileUpload.addEventListener("drop", onDrop);

    fileUploads.set(element, fileUpload);
}

export function dispose(dotnet, element) {
    const fileUpload = fileUploads.get(element);

    if(!fileUpload) return;

    fileUpload.removeEventListener("dragenter", onDragHover);
    fileUpload.removeEventListener("dragover", onDragHover);
    fileUpload.removeEventListener("dragleave", onDragLeave);
    fileUpload.removeEventListener("drop", onDrop);

    fileUploads.delete(element);
}
