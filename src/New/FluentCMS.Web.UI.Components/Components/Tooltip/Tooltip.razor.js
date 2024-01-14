import { computePosition, autoUpdate } from 'https://cdn.jsdelivr.net/npm/@floating-ui/dom@1.5.4/+esm';

const tooltips = new Map();

export function update(dotnet, element, config) { }

export function initialize(dotnet, element, config) {
    dispose(dotnet, element);

    const target = element.previousElementSibling;

    function onMouseEnter() {
        element.classList.add('f-tooltip-show')
    }

    target.addEventListener('mouseenter', onMouseEnter);

    function onMouseLeave() {
        element.classList.remove('f-tooltip-show')
    }

    target.addEventListener('mouseleave', onMouseLeave);

    const cleanup = autoUpdate(target, element, () => {
        const options = {
            placement: config.placement
        }

        computePosition(target, element, options)
            .then(({ x, y }) => {
                Object.assign(element.style, {
                    left: `${x}px`,
                    top: `${y}px`
                })
            })
    });

    const tooltip = {
        dispose() {
            target.removeEventListener('mouseenter', onMouseEnter);
            target.removeEventListener('mouseleave', onMouseLeave);

            cleanup()
        }
    }

    tooltips.set(element, tooltip);
}

export function dispose(dotnet, element) {
    const tooltip = tooltips.get(element);

    tooltip?.dispose();

    tooltips.delete(element);
}

