const confirms = new Map();

export function close(dotnet, element) {
    const confirm = confirms.get(element);

    confirm.hide();
}

export function open(dotnet, element) {
    const confirm = confirms.get(element);

    confirm.show();
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const Modal = window.Flowbite.default.Modal;

    const confirm = new Modal(element, {
        backdrop: config.static ? 'static' : 'dynamic',
        onHide: () => {
            dotnet.invokeMethodAsync("Close");
        },
    });

    confirms.set(element, confirm);

    if (config.open) {
        confirm.show();
    }
}

export function dispose(dotnet, element) {
    const confirm = confirms.get(element);

    confirm?.destroy();

    confirms.delete(element);
}
