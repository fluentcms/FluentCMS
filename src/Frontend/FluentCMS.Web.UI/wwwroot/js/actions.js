export function initializeActions(element, actions) {
    element.querySelectorAll('[data-action]').forEach(action => {
        action.addEventListener('click', () => {
            console.log('actions: ', {action: action.dataset.action, actions})
            actions[action.dataset.action](action)
        })
    })
}