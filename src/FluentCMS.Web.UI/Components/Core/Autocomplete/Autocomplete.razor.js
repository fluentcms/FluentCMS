import '../../../js/tom-select.js'

const autocompletes = new Map();

export function update(dotnet, element, config) {
    const autocomplete = autocompletes.get(element);

    if ('disabled' in config) {
        if (config.disabled) {
            autocomplete.disable();
        } else {
            autocomplete.enable();
        }
    }

    if ('value' in config) {
        autocomplete.setValue(config.value, true);
    }

    if ('options' in config) {
        const isOpen = autocomplete.isOpen;

        autocomplete.clear(true);

        autocomplete.clearOptions();

        autocomplete.addOptions(config.options, false);

        autocomplete.refreshOptions(isOpen);
    }
}

export function initialize(dotnet, element) {
    terminate(dotnet, element);

    const autocomplete = new TomSelect(element, {
        valueField: 'id',
        labelField: 'title',
        searchField: ['title'],
        allowEmptyOption: true,
        onChange: () => {
            dotnet.invokeMethodAsync("UpdateValue", autocomplete.items);
        },
    });

    autocompletes.set(element, autocomplete);
}

export function terminate(dotnet, element) {
    const autocomplete = autocompletes.get(element);

    autocomplete?.destroy();

    autocompletes.delete(element);
}
