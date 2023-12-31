import { computePosition, autoUpdate } from '../../../js/floating-ui-dom.min.js'

const tooltips = new Map();
const cleanups = new Map();

export function update(dotnet, element, config) {
    const tooltip = tooltips.get(element)

    console.log('update')
}
export function initialize(dotnet, target, element) {
    terminate(dotnet, element)
    if(!target) {
        target = element.previousElementSibling;
    }

    const cleanup = autoUpdate(target, element, () => {
        computePosition(target, element).then(({x, y}) => {
            console.log(x, y)
            Object.assign(tooltip, {
                left: `${x}px`,
                top: `${y}px`
            })
        })
    })

    tooltips.set(element, cleanup)
    cleanups.set(element, cleanup)
}

export function terminate() {
    const tooltip = tooltips.get(element)

    tooltips.delete(element);
    cleanups.delete(element);
}

export function dispose(dotnet, element) {
    const cleanup = cleanups.get(element)

    cleanup()
}
