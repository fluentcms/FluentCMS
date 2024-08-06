import {request, hydrate, initializeSortable, initColumns} from './request.js'
import './sortable.js'

let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

let responsiveMode = null

const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')

// request('TEST', {abc: '123'})

let frameDocument;
function getFrameDocument() {
    return new Promise(resolve => {
        iframeElement.onload = () => {
            frameDocument = iframeElement.contentDocument;
            resolve(frameDocument)
        }
    })
}

let updateForm = document.querySelector("#f-page-editor-form-update")

function save() {
    const sections = {}

    frameDocument.querySelectorAll('.f-section').forEach(section => {
        sections[section.dataset.name] = []

        section.querySelectorAll('.f-plugin-container').forEach(plugin => {
            sections[section.dataset.name].push(plugin.dataset)
        })
    })

    var updateInputs = '<input type="hidden" name="Model.Submitted" value="true" />'
    let deleteIndex = 0;
    let newPluginsIndex = 0;
    let updatedPluginsIndex = 0;
    let pluginOrder = 0;
    for(let section in sections) {
        for(let index in sections[section]) {
            const plugin = sections[section][index].id
            const cols = sections[section][index].cols
            const colsMd = sections[section][index].colsMd
            const colsLg = sections[section][index].colsLg
            const definitionId = sections[section][index].definitionId
            const deleted = sections[section][index].deleted

            if(deleted) {
                updateInputs += `
                    <input type="hidden" name="Model.DeleteIds[${deleteIndex}]" value="${plugin}" />
                `
                deleteIndex++;
            }
            else if(plugin == '00000000-0000-0000-0000-000000000000') {
                // PageId will be filled in backend
                updateInputs += `
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].PageId" value="00000000-0000-0000-0000-000000000000" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].DefinitionId" value="${definitionId}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Order" value="${pluginOrder}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Cols" value="${cols}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsMd" value="${colsMd}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsLg" value="${colsLg}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Section" value="${section}" />
                `
                pluginOrder++;
                newPluginsIndex++;
            }
            else {
                updateInputs += `
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Section" value="${section}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Id" value="${plugin}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Order" value="${pluginOrder}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Cols" value="${cols}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsMd" value="${colsMd}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsLg" value="${colsLg}" />
                `
                pluginOrder++;
                updatedPluginsIndex++;
            }
        }
    }

    updateForm.querySelector('#f-update-form-inputs').outerHTML = updateInputs
    setTimeout(() => updateForm.submit())
}

function updateResponsive(mode, silent) {
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

function submitForm(form, data) {
    for(let key in data) {
        form.querySelector(`[name="${key}"]`).value = data[key]
    }
    form.submit()
}

function updateResizerPosition() {
    resizerElement.style.left = (iframeElement.getBoundingClientRect().right + 2) + 'px'

    for(let key of Object.keys(iframeElement.contentWindow.sections)) {
        iframeElement.contentWindow.sections[key].updateSize()
    }
}
function initializeResponsive() {
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


async function onInit() {
    const frameDocument = await getFrameDocument()

    initializeResponsive()

    hydrate(frameDocument)
    hydrate(document)
    initializeSortable(frameDocument)
    initColumns(frameDocument)
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

onInit()