import {initializeActions} from './actions.js'
import { onCloseOffcanvas , onPageSettings, onPagesList } from './toolbar-actions.js'
import { onPluginEdit } from './plugin-actions.js'

const actions = {
    'pages-list': onPagesList,
    'page-settings': onPageSettings,
    'plugin-edit': onPluginEdit,
    'close-offcanvas': onCloseOffcanvas,
}

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
    initializeActions(document, actions)
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


