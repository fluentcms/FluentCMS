const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')
let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

let responsiveMode = null

export function updateResponsive(mode, silent) {
    let frameDocument = document.querySelector('.f-page-editor-iframe').contentDocument

    document.querySelector(`.f-toolbar-responsive-button-active`).classList.remove('f-toolbar-responsive-button-active')

    responsiveMode = mode;
    document.querySelector(`[data-action="responsive-${mode}"]`).classList.add('f-toolbar-responsive-button-active');

    if(responsiveMode === 'default') {
        iframeElement.style.width = '100%';
        delete pageEditorElement.dataset['responsiveMode']
        delete frameDocument.querySelector('body').dataset.responsiveMode
    }
    else {
        pageEditorElement.dataset.responsiveMode = responsiveMode
        frameDocument.querySelector('body').dataset.responsiveMode = mode
        if(!silent) {
            if(responsiveMode == 'tablet') {
                iframeElement.style.width = '768px';
            }
            if(responsiveMode == 'mobile') {
                iframeElement.style.width = '440px';
            }
            if(responsiveMode == 'desktop') {
                iframeElement.style.width = '1024px';
            }
            updateResizerPosition()
        }
    }
}
export function updateResizerPosition() {
    resizerElement.style.left = (iframeElement.getBoundingClientRect().right + 2) + 'px'
}

export function initializeResponsive() {
    let dragging = false;

    resizerElement.addEventListener('mousedown', (e) => {
        dragging = true;
    })

    function onMouseMove(e) {
        if(dragging) {
            const iframeWidth = iframeElement.getClientRects()[0].width

            if(iframeWidth > 992) {
                updateResponsive('desktop', true)
            } else if(iframeWidth > 480) {
                updateResponsive('tablet', true)
            } else {
                updateResponsive('mobile', true)
            }

            const delta = e.movementX
            iframeElement.style.width = (iframeWidth + 2 * delta) + 'px'

            updateResizerPosition()
        }
    }

    function onMouseUp() {
        dragging = false;
    }

    window.addEventListener('mousemove', onMouseMove)
    iframeElement.contentWindow.addEventListener('mousemove', onMouseMove)

    window.addEventListener('mouseup', onMouseUp)
    iframeElement.contentWindow.addEventListener('mouseup', onMouseUp)
}

