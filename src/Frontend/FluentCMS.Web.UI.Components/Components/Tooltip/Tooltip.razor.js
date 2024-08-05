import '/_content/FluentCMS.Web.UI.Components/js/floating-ui.core.js'
import '/_content/FluentCMS.Web.UI.Components/js/floating-ui.dom.js'

const tooltips = new Map();

export function update(dotnet, element, config) {

}

export function initialize(dotnet, element, config) {
    console.log('initialize', element, window.FloatingUIDOM)
    dispose(dotnet, element);

    const target = element;

    if (!element) return;

    const trigger = element.previousElementSibling;

    const options = {
        placement: config.placement,
        middleware: [
            FloatingUIDOM.offset(6),
            FloatingUIDOM.flip(), 
            FloatingUIDOM.shift({padding: 5}), 
        ]
    };

    const cleanup = FloatingUIDOM.autoUpdate(trigger, target, () => {
        FloatingUIDOM.computePosition(trigger, target, options).then(({x, y}) => {
            Object.assign(element.style, {
                left: `${x}px`,
                top: `${y}px`,
            });
        });
    })

    function showTooltip() {
        Object.assign(element.style, {
            display: 'block'
        });
    } 
    
    function hideTooltip() {
        Object.assign(element.style, {
            display: 'none'
        });
    }

    hideTooltip()
    trigger.addEventListener('mouseenter', showTooltip)
    trigger.addEventListener('mouseleave', hideTooltip)
    
    const tooltip = {
        destroy() {
            trigger.removeEventListener('mouseenter', showTooltip)
            trigger.removeEventListener('mouseleave', hideTooltip)
            cleanup()
        }
    }
    

    tooltips.set(element, tooltip);
}

export function dispose(dotnet, element) {
    const tooltip = tooltips.get(element);

    tooltip?.destroy();

    tooltips.delete(element);
}
