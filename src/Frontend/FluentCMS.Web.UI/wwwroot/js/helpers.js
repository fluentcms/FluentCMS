import { updateResizerPosition } from "./responsive.js"
let pageEditorElement = document.querySelector('.f-page-editor')

export function closeOffcanvas() {
    delete pageEditorElement.dataset['offcanvasId']    
}

export function onHideSidebar() {
    // alert('enable responsive buttons')
    pageEditorElement.classList.add('f-page-editor-sidebar-close')
    pageEditorElement.classList.remove('f-page-editor-sidebar-open')
    setTimeout(() => {
        updateResizerPosition()
    }, 300)
}

export function openOffcanvas(id, width = 400) {
    console.log('openOffcanvas')
    if(pageEditorElement.dataset.offcanvasId == id) {
        closeOffcanvas()
    } else {
        pageEditorElement.dataset.offcanvasId = id
        document.querySelector('.f-page-editor-offcanvas').setAttribute('style', '--f-offcanvas-width: ' + width + 'px;')
        onHideSidebar()
    }
}