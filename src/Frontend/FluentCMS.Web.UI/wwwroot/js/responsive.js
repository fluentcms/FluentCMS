const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')
let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

let responsiveMode = null


export function updateResponsive(mode, silent) {
    if(responsiveMode == mode) {
        if(silent) return;
        responsiveMode = null;
        document.querySelector(`[data-action="responsive-${mode}"]`).classList.remove('f-toolbar-responsive-button-active');
    }
    else {
        if(responsiveMode)
            document.querySelector(`[data-action="responsive-${responsiveMode}"]`).classList.remove('f-toolbar-responsive-button-active')

        responsiveMode = mode;
        document.querySelector(`[data-action="responsive-${mode}"]`).classList.add('f-toolbar-responsive-button-active');
    }

    if(responsiveMode) {
        pageEditorElement.dataset.responsiveMode = responsiveMode
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

    } else {
        delete pageEditorElement.dataset['responsiveMode']
        iframeElement.style.width = '100%';

    }
}
export function updateResizerPosition() {
    resizerElement.style.left = (iframeElement.getBoundingClientRect().right + 2) + 'px'

    for(let key of Object.keys(window.sections)) {
        window.sections[key].updateSize()
    }
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

            if(document.querySelector('.f-page-editor-sidebar').getBoundingClientRect().x < e.x) {
                dragging = false;
            }
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

