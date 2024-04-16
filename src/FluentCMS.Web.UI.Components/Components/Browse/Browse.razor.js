const browses = new Map();

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

    const browse = element;
 
    browse.addEventListener("dragenter", onDragHover);
    browse.addEventListener("dragover", onDragHover);
    browse.addEventListener("dragleave", onDragLeave);
    browse.addEventListener("drop", onDrop);

    browses.set(element, browse);
}

export function dispose(dotnet, element) {
    const browse = browses.get(element);

    browse?.removeEventListener("dragenter", onDragHover);
    browse?.removeEventListener("dragover", onDragHover);
    browse?.removeEventListener("dragleave", onDragLeave);
    browse?.removeEventListener("drop", onDrop);

    browses.delete(element);
}
