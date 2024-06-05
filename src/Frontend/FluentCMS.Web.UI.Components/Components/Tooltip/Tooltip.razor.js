const tooltips = new Map();

export function update(dotnet, element, config) {

}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const target = element;

    if (!element) return;

    const trigger = element.previousElementSibling;

    const options = {
        placement: config.placement,
        triggerType: 'hover',
    };

    const tooltip = new window.Flowbite.default.Tooltip(target, trigger, options);

    tooltips.set(element, tooltip);
}

export function dispose(dotnet, element) {
    const tooltip = tooltips.get(element);

    tooltip?.destroy();

    tooltips.delete(element);
}
