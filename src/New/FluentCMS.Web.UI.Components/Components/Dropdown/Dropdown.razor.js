const dropdowns = new Map();

export function update(dotnet, element, config) {
    const dropdown = dropdowns.get(element);

    if ('open' in config) {
        if (config.open) {
            if (!dropdown.isVisible()) {
                dropdown.show();
            }
        }
        else {
            if (dropdown.isVisible()) {
                dropdown.hide();
            }
        }
    }
}

export function initialize(dotnet, element) {
    dispose(dotnet, element);

    const target = element.lastElementChild;

    const trigger = element.firstElementChild;

    const options = {
        onHide: () => {
            dotnet.invokeMethodAsync("Update", false);
        },
        onShow: () => {
            dotnet.invokeMethodAsync("Update", true);
        }
    };

    const dropdown = new window.Flowbite.default.Dropdown(target, trigger, options);

    dropdowns.set(element, dropdown);
}

export function dispose(dotnet, element) {
    const dropdown = dropdowns.get(element);

    dropdown?.destroy();

    dropdowns.delete(element);
}
