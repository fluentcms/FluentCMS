import {initPluginActions} from './plugin-actions.js'
function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}
function onEditContentButtonClicked() {
    document.body.classList.toggle('f-edit-content')
}

function init() {
    if(window.editButton) {
        editButton.addEventListener('click', onEditButtonClicked)    
        editContentButton.addEventListener('click', onEditContentButtonClicked)    
    }
    initPluginActions(document)
}

function destroy () {
    if(window.editButton) {
        editButton.removeEventListener('click', onEditButtonClicked)    
        editContentButton.removeEventListener('click', onEditContentButtonClicked)    

    }
}

window.addEventListener('fluentcms:afterenhanced', init)
window.addEventListener('fluentcms:beforeenhanced', destroy)
init()


