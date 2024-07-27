import { updateResizerPosition } from "./responsive.js"
let offcanvasElement = document.querySelector('.f-page-editor-offcanvas')
let pageEditorElement = document.querySelector('.f-page-editor')
let iframeElement = document.querySelector('.f-page-editor-iframe')
let frameDocument;

export function getFrameDocument() {
    return new Promise(resolve => {
        iframeElement.onload = () => {
            frameDocument = iframeElement.contentDocument;
            resolve(frameDocument)
        }
    })
}

export function debounce(cb, timeout = 300) {
    let timer;
    return (...args) => {
        if(timer) clearTimeout(timer)
        timer = setTimeout(() => {
            cb(args)
        }, timeout)
    }
}

function getPageMode() {
    const isPageEditorMode = window.location.href.includes('?pageEdit=true')

    if(isPageEditorMode) return 'edit'
    return 'default'
}

export function closeOffcanvas() {
    delete offcanvasElement.dataset['offcanvasId']    
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
    if(offcanvasElement.dataset.offcanvasId == id) {
        closeOffcanvas()
    } else {
        offcanvasElement.dataset.offcanvasId = id
        offcanvasElement.setAttribute('style', '--f-offcanvas-width: ' + width + 'px;')

        if(getPageMode() === 'edit')
            onHideSidebar()
    }
}

export function parseStyles(styleStr, dataset) {
    let object = {}
    let stylesSplitted = styleStr.split(';').filter(Boolean);

    for(let style of stylesSplitted) {
        const [key, value] = style.split(':').map(x => x.trim());
        object[key] = value
    }

    for(let key in dataset) {
        if(dataset[key] != 'undefined')
            object[key[0].toUpperCase() + key.slice(1)] = dataset[key]
    }

    return object
}