const modals = new Map();

export function close(dotnet, element) {
    const modal = modals.get(element);

    modal.hide();
}

export function open(dotnet, element) {
    const modal = modals.get(element);

    modal.show(); 
    element.focus();
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const Modal = window.Flowbite.default.Modal;

    const modal = new Modal(element, {
        backdrop: config.static ? 'static' : 'dynamic',
        onHide: () => {
            dotnet.invokeMethodAsync("Close");
        },
    });

    modals.set(element, modal);
}

export function dispose(dotnet, element) {
    const modal = modals.get(element);

    modal?.destroy();

    modals.delete(element);
}
