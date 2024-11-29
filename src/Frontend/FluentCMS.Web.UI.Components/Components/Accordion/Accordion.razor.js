const accordions = new Map();

export function close(dotnet, element) {
    const accordion = accordions.get(element);

    if (!accordion.getItem().active) return;

    accordion.close();
}

export function open(dotnet, element) {
    const accordion = accordions.get(element);

    if (accordion.getItem().active) return;

    accordion.open();
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const Accordion = window.Flowbite.default.Accordion;

    const accordion = new Accordion(
        element,
        [
            {
                triggerEl: element.querySelector('* > h2'),
                targetEl: element.querySelector('* > div'),
            }
        ],
        {
            onOpen: (item) => {
                dotnet.invokeMethodAsync("Update", true);
            },
            onClose: (item) => {
                dotnet.invokeMethodAsync("Update", false);
            },
        }
    );

    accordions.set(element, accordion);
}

export function dispose(dotnet, element) {
    const accordion = accordions.get(element);

    accordion?.destroy();

    accordions.delete(element);
}
