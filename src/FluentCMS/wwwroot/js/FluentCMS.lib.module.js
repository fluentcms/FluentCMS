
const acceptHeader = "blazor-enhanced-nav=on"

const eventNamePrefix = 'blazor:'
const eventNames = {
    beforeenhanceload: 'beforeenhanceload',
    enhanceload: 'enhanceload',
    beforewebstarted: 'beforewebstarted',
    afterwebstarted: 'afterwebstarted',
}

const originalFetch = window.fetch
window.fetch = async (...args) => {
    const fetchOptions = args[1]

    if(fetchOptions.headers && fetchOptions.headers['accept'] && fetchOptions.headers['accept'].includes(acceptHeader)) {
        dispatchEvent(new CustomEvent(eventNamePrefix + eventNames.beforeenhanceload))
    }
    
    return originalFetch(...args)
}

function onEnhancedLoad() {
    console.log('enhanced load')
    dispatchEvent(new CustomEvent(eventNamePrefix + eventNames.enhanceload))
}

export function beforeWebStarted(blazor) {
    dispatchEvent(new CustomEvent(eventNamePrefix + eventNames.beforewebstarted))
}

export function afterWebStarted(blazor) {
    console.log('after web started')
    dispatchEvent(new CustomEvent(eventNamePrefix + eventNames.afterwebstarted))

    blazor.addEventListener('enhancedload', onEnhancedLoad)
}
