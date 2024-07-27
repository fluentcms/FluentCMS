export function ActionManager(element, options = {}) {
    let actions = options.actions ?? {}

    function onHandleEvent(el) {
        actions[el.dataset.action](el)
    }

    function init() {
        element.querySelectorAll('[data-action]').forEach(el => {
            let togglerEvent = el.dataset.toggler ?? 'click'
            el.addEventListener(togglerEvent, (event) => onHandleEvent(el))
        })
    }

    function register(key, value) {
        actions[key] = value
    }
    
    return {
        register,
        init,
        destroy() {
            // element.querySelectorAll('[data-action]').forEach(el => {
            //     let togglerEvent = el.dataset.toggler ?? 'click'
            //     el.removeEventListener(togglerEvent, onHandleEvent)
            // })
        }
    }

}