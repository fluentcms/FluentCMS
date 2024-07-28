let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

let responsiveMode = null

const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')

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

const actions = {
    'cancel-edit-mode'() {
        actions["hide-sidebar"]()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
    },
    'save-edit-mode'() {
        actions["hide-sidebar"]()
        save()
    },
    'responsive-mobile'() {
        updateResponsive('mobile')
    },
    'responsive-tablet'() {
        updateResponsive('tablet')
    },
    'responsive-desktop'() {
        updateResponsive('desktop')
    },
    'plugin-container-action-delete'(el) {
        const id = el.parentElement.parentElement.dataset.id

        if(id == '00000000-0000-0000-0000-000000000000') {
            el.parentElement.parentElement.parentElement.remove()
        } else {
            el.parentElement.parentElement.parentElement.dataset.deleted = true
            el.parentElement.parentElement.parentElement.classList.add('f-hidden')
        }
    },
    'show-sidebar'() {
        pageEditorElement.classList.remove('f-page-editor-sidebar-close')
        pageEditorElement.classList.add('f-page-editor-sidebar-open')
        setTimeout(() => {
            updateResizerPosition()
        }, 300)
    },
    'hide-sidebar'() {
        pageEditorElement.classList.add('f-page-editor-sidebar-close')
        pageEditorElement.classList.remove('f-page-editor-sidebar-open')
        setTimeout(() => {
            updateResizerPosition()
        }, 300)
    }
}

function initializeActions(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        action.addEventListener('click', () => {
            actions[action.dataset.action](action)
        })
    })
    initPluginActions(element)
}

function submitForm(form, data) {
    for(let key in data) {
        form.querySelector(`[name="${key}"]`).value = data[key]
    }
    form.submit()
}

function initializeSortable(frameDocument) {
    const sectionElements = frameDocument.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag',
        });

    });

    new Sortable(document.querySelector('.f-plugin-definition-list'), {
        animation: 150,
        group: {
            name: 'shared',
            pull: 'clone',
            put: false
        },
        sort: false,
        draggable: '.f-plugin-definition-item',
        onEnd(event) {
            if(event.from === event.to) return;
            const definitionId = event.clone.dataset.id
            const item = event.item
            const sectionName = event.to.dataset.name

            createPlugin({ definitionId, item, sectionName })
        }
    });
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

function createPlugin({definitionId, sectionName, item}) {
    item.classList.remove('f-plugin-definition-item')
    item.classList.add('f-plugin-container')
    item.dataset.cols = 12
    item.dataset.colsMd = 0
    item.dataset.colsLg = 0

    item.dataset.id = '00000000-0000-0000-0000-000000000000';
    item.dataset.definitionId = definitionId;

    const name = item.querySelector('.f-name').textContent
    // const description = item.querySelector('.f-description').textContent
    const description = "You can preview new plugins after save"

    const pluginContainerActionsTemplate = `<div class="f-plugin-container-actions">
        <div class="f-plugin-container-action-text f-plugin-container-action-drag">
            <svg class="icon" width="24" height="24" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill="currentColor" d="M18 11V8l4 4l-4 4v-3h-5v5h3l-4 4l-4-4h3v-5H6v3l-4-4l4-4v3h5V6H8l4-4l4 4h-3v5z"/></svg>
            ${name}
        </div>
        <button data-action="plugin-container-action-delete">
            <svg class="icon" aria-hidden="true" xmlns="http://www.w3.org/2000/svg" width="24" height="24" fill="currentColor" viewBox="0 0 24 24">
                <path fill-rule="evenodd" d="M8.586 2.586A2 2 0 0 1 10 2h4a2 2 0 0 1 2 2v2h3a1 1 0 1 1 0 2v12a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V8a1 1 0 0 1 0-2h3V4a2 2 0 0 1 .586-1.414ZM10 6h4V4h-4v2Zm1 4a1 1 0 1 0-2 0v8a1 1 0 1 0 2 0v-8Zm4 0a1 1 0 1 0-2 0v8a1 1 0 1 0 2 0v-8Z" clip-rule="evenodd">
                </path>
            </svg>
        </button>
</div>`


    const pluginContainerContentTemplate = `<div class="f-plugin-container-content">
    <div class="f-plugin-container-content-new">
        <div class="f-name">${name}</div>
        <div class="f-description">${description}</div>
    </div>
</div>`

    item.innerHTML = [pluginContainerActionsTemplate, pluginContainerContentTemplate].join('')

    setTimeout(() => {
        initializeActions(item)
        iframeElement.contentWindow.sections[sectionName].append(item)
    })
}

async function onInit() {
    const frameDocument = await getFrameDocument()

    initializeResponsive()

    initializeActions(frameDocument)
    initializeActions(document)
    initializeSortable(frameDocument)
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

document.addEventListener('fluentcms:init', () => {
    onInit()
})
