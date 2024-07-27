import { ActionManager } from './actions.js'
import { onCloseOffcanvas , onPageSettings, onPagesList } from './toolbar-actions.js'
import { onPluginEdit } from './plugin-actions.js'
import { lifecycle } from './lifecycle.js'

function reload() { 
    setTimeout(() => {
        location.reload()
    }, 300)
}
const actions = {
    // 'pages-list': onPagesList,
    // 'page-settings': onPageSettings,
    // 'plugin-edit': (el) => ,
    'close-offcanvas': onCloseOffcanvas,
}

function onEditButtonClicked() {
    window.location.href = window.location.href + '?pageEdit=true'
}

function onEditContentButtonClicked() {
    document.body.classList.add('f-edit-content')
}

function onDoneButtonClicked() {
    document.body.classList.remove('f-edit-content')
}

function init() {
    if(window.editButton) {
        editButton.addEventListener('click', onEditButtonClicked)    
        editContentButton.addEventListener('click', onEditContentButtonClicked)    
        doneButton.addEventListener('click', onDoneButtonClicked)
    }
    const actionManager = ActionManager(document)
    
    actionManager.register('plugin-edit', onPluginEdit)
    actionManager.register('close-offcanvas', onCloseOffcanvas)

    lifecycle.on('reload', () => {
        window.location.reload() 
    })
}

function destroy () {
    if(window.editButton) {
        editButton.removeEventListener('click', onEditButtonClicked)    
        editContentButton.removeEventListener('click', onEditContentButtonClicked)    
        doneButton.removeEventListener('click', onDoneButtonClicked)
    }
}

window.addEventListener('fluentcms:afterenhanced', init)
window.addEventListener('fluentcms:beforeenhanced', destroy)

init()


