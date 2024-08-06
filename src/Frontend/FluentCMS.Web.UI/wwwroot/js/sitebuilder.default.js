function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}

function init() {
    if(window.editButton) {
        editButton.addEventListener('click', onEditButtonClicked)    
    }
}

function destroy () {
    if(window.editButton) {
        editButton.removeEventListener('click', onEditButtonClicked)    
    }
}

window.addEventListener('fluentcms:afterenhanced', init)
window.addEventListener('fluentcms:init', init)


window.addEventListener('fluentcms:beforeenhanced', destroy)
