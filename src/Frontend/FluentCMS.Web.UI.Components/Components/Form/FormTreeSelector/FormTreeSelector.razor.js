import {TreeSelector} from '/_content/FluentCMS.Web.UI.Components/js/tree-selector.js'
import { debounce } from '/_content/FluentCMS.Web.UI.Components/js/helpers.js'

const treeSelectors = new Map();

export function update(dotnet, element, config) {
    const instance = treeSelectors.get(element)

    if ('value' in config) {
        instance.setValue(config.value);
    }
}

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    let instance = TreeSelector(element, {
        options: config.items,
        cssPrefix: 'f-tree-selector',
        value: config.value,
        onChange(value) {
            dotnet.invokeMethodAsync('UpdateValue', value)
        },
    })

    treeSelectors.set(element, instance);
}

export function dispose(dotnet, element) {
    const treeSelector = treeSelectors.get(element);

    treeSelector?.dispose();

    treeSelectors.delete(element);
}
