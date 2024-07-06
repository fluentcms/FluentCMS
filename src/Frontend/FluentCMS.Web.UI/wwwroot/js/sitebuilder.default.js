function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}

function init() {
    if(window.editButton) {
        editButton.addEventListener('click', onEditButtonClicked)    
    }
    document.querySelectorAll('[data-action]').forEach(el => {
        el.addEventListener('click', () => {
            if(el.dataset.action == 'plugin-edit') {
                window.location.href = window.location.pathname + el.dataset.url
            }
        })
    })
}

function destroy () {
    if(window.editButton) {
        editButton.removeEventListener('click', onEditButtonClicked)    
    }
}

window.addEventListener('fluentcms:afterenhanced', init)
window.addEventListener('fluentcms:init', init)


window.addEventListener('fluentcms:beforeenhanced', destroy)
