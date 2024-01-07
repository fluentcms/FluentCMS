import { computePosition, autoUpdate } from 'https://cdn.jsdelivr.net/npm/@floating-ui/dom@1.5.3/+esm'

const cleanups = new Map();

export function update(dotnet, element, config) {
    //
}


export function initialize(dotnet, target, element, config) {
    terminate(dotnet, element)
    if(!target || target.__internalId == null) {
        target = element.previousElementSibling;
    }

    function onMouseEnter(e) {
        element.classList.add('f-tooltip-show')
    }


    function onMouseLeave(e) {
        element.classList.remove('f-tooltip-show')
    }

    target.addEventListener('mouseenter', onMouseEnter)
    target.addEventListener('mouseleave', onMouseLeave)

    const cleanupFloatingUi = autoUpdate(target, element, () => {
        computePosition(target, element, {
            placement: config.placement
        }).then(({x, y}) => {
            Object.assign(element.style, {
                left: `${x}px`,
                top: `${y}px`
            })
        })
    })

    function cleanup() {
        target.removeEventListener('mouseenter', onMouseEnter)
        target.removeEventListener('mouseleave', onMouseLeave)

        cleanupFloatingUi()
    }

    cleanups.set(element, cleanup)
}

export function terminate(dotnet, element) {
    cleanups.delete(element);
}

export function dispose(dotnet, element) {
    const cleanup = cleanups.get(element)

    cleanup()
}
