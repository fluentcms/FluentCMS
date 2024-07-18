let responsiveMode = null;
let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')

export function updateResizerPosition() {
    resizerElement.style.left = (iframeElement.getBoundingClientRect().right + 2) + 'px'

    for(let key of Object.keys(iframeElement.contentWindow.sections)) {
        iframeElement.contentWindow.sections[key].updateSize()
    }
}

export function updateResponsive(mode, silent) {
    if(responsiveMode == mode && silent)
        return;        
     
    // toggle classes
    if(responsiveMode)
        document.querySelector(`[data-action="responsive-${responsiveMode}"]`).classList.remove('f-page-editor-header-button-primary')

    responsiveMode = mode;
    document.querySelector(`[data-action="responsive-${mode}"]`).classList.add('f-page-editor-header-button-primary');


    if(responsiveMode) {
        pageEditorElement.dataset.responsiveMode = responsiveMode
        if(!silent) {
            if(responsiveMode == 'tablet') {
                iframeElement.style.width = '768px';
            }
            if(responsiveMode == 'mobile') {
                iframeElement.style.width = '440px';
            }
            if(responsiveMode == 'laptop') {
                iframeElement.style.width = '1024px';
            }
            if(responsiveMode == 'large') {
                delete pageEditorElement.dataset['responsiveMode']
                iframeElement.style.width = '100%';
            }
            updateResizerPosition()
        }
    }
}