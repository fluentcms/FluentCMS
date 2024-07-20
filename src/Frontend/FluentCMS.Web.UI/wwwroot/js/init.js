import { updateResponsive, updateResizerPosition } from "./responsive.js"
import Sortable from "./sortable.js"
import {initPluginActions} from './plugin-actions.js'

let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')

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
    setTimeout(() => {
        const body = new FormData(updateForm)

        fetch(window.location.href, {
            method: 'POST',
            body
        })
    })
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

            console.log(event)

            createPlugin({ definitionId, item, index: event.newIndex, sectionName })
        }
    });
}

function initializeResponsive() {
    updateResponsive('large', false)
    let dragging = false;

    resizerElement.addEventListener('mousedown', (e) => {
        dragging = true;
    })

    function onMouseMove(e) {
        if(dragging) {
            const iframeWidth = iframeElement.getClientRects()[0].width

            if(iframeWidth > 1920) {
                updateResponsive('large', true)
            } else if(iframeWidth > 992) {
                updateResponsive('laptop', true)
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

function createPlugin({definitionId, sectionName, index, item}) {
    item.classList.remove('f-plugin-definition-item')
    item.classList.add('f-plugin-container')
    const cols = 12
    const colsMd = 0
    const colsLg = 0

    const pageId = '00000000-0000-0000-0000-000000000000';

    setTimeout(() => {
        // _handler: UpdatePluginForm
        // __RequestVerificationToken: CfDJ8PuVbaR1sHBOvuLydAdhPdS6nFFhM7eu29IOqf-5F1JRXcEXCWrMS7nUql8VqVNT4ctfGixG1DwfugOxVUgVPp6nEculRq8apOwX66kkwTmzoQ9_GQld_t89NFl1QQ5-W_EfAun8bDQLQNCWQ5_d-njmF1yG0yZw_Qk7iMJbyJGR7y59tE9_vUSG2Y9nC4NJwg

        const requestBody = {
            '_handler': 'UpdatePluginForm',
            '__RequestVerificationToken': document.querySelector('[name="__RequestVerificationToken"]').value,
            'Model.CreatePlugins[0].PageId': pageId,
            'Model.CreatePlugins[0].DefinitionId': definitionId,
            'Model.CreatePlugins[0].Order': index - 1,
            'Model.CreatePlugins[0].Cols': cols,
            'Model.CreatePlugins[0].ColsMd': colsMd,
            'Model.CreatePlugins[0].ColsLg': colsLg,
            'Model.CreatePlugins[0].Section': sectionName,
            "Model.Submitted": true,
            'Model.UpdatePlugins': '[]', // TODO
            'Model.DeleteIds': '[]', // TODO
        }

        const body = new FormData()
        for(let key in requestBody) {
            body.append(key, requestBody[key])
            // body.set('Model.DeleteIds', [])
        }

        fetch(window.location.href, {
            method: 'POST',
            body
        }).then(async res => {
            
            iframeElement.contentWindow.location.reload()
            // const html = await fetch(window.location.href).then(res => res.text())
            // document.documentElement.innerHTML = html
            setTimeout(() => {
                onInit()
            })
        })
    })
}

function parseStyles(styleStr, dataset) {
    let object = {}
    let stylesSplitted = styleStr.split(';').filter(Boolean);
    console.log({stylesSplitted})

    for(let style in stylesSplitted) {
        const [key, value] = style.split(':').map(x => x.trim());
        console.log({key , value})
        object[key] = value
    }

    console.log({dataset})
    for(let key in dataset) {
        object[key[0].toUpperCase() + key.slice(1)] = dataset[key]
    }

    console.log(object)
    return object
}

async function saveColumns() {
    console.log('saveColumns')
    let columns = {}
    iframeElement.contentDocument.querySelectorAll('.f-row').forEach(row => {
        // columns[el.id.replace('column-', '')] = el.dataset
        let colIndex = 0 
        row.querySelectorAll('.f-column').forEach(el => {
            const key = el.id.replace('column-', '')
            columns[key] = {}
            columns[key].dataset = el.dataset
            columns[key].order = colIndex++;
            columns[key].rowId = row.id.replace('row-', '');
            columns[key].style = el.getAttribute('style') ?? '';
        })
    
    })

    let requestBody = {
        '_handler': "ColumnUpdateForm",
    }

    let index = 0;
    for(let id in columns) {
        const column = columns[id]
        requestBody[`ColumnUpdateModel.Columns[${index}].Id`] = id
        requestBody[`ColumnUpdateModel.Columns[${index}].Order`] = column.order
        requestBody[`ColumnUpdateModel.Columns[${index}].RowId`] = column.rowId
        const styles = parseStyles(column.style, column.dataset)

        for(let key in styles) {
            requestBody[`ColumnUpdateModel.Columns[${index}].Styles[${key}]`] = styles[key] 
        }

        index++;
    }
    await request(requestBody)
    // alert(JSON.stringify(columns))
    // Order based on index
    
}


async function request(values) {
    const url = window.location.href
    const body = new FormData()
            
    // body.set('_handler', 'UpdatePluginForm');
    body.set('__RequestVerificationToken', document.querySelector('[name="__RequestVerificationToken"]').value)

    for(let key in values) {
        body.set(key, values[key])
    }

    await fetch(url, {
        method: 'POST',
        body
    }).then(res => {
        res.text()
    }).then(res => {
        console.log(res)
    });

    // update iframe content
    console.log(iframeElement.contentDocument.body)
    const response = await fetch(url.replace('pageEdit', 'pagePreview')).then(res => res.text())
    iframeElement.contentDocument.documentElement.innerHTML = response
    setTimeout(() => {
        initializeActions(iframeElement.contentDocument)
        initializeColumns()
    })
}

function initializeColumns() {
    iframeElement.contentWindow.initializeColumns({
        onResize: () => {
            saveColumns()
        }
    })
}
async function onAddSection(el) {
    // console.log('add section')
    await request({
        '_handler': "AddSectionForm",
        'AddSectionModel.Submitted': true,
        'AddSectionModel.Order': el.dataset.order ?? 0
    })
    
}

async function onAddColumn(el) {
    await request({
        '_handler': "AddColumnForm",
        'AddColumnModel.Submitted': true,
        'AddColumnModel.Mode': el.dataset.mode,
        'AddColumnModel.Order': el.dataset.order ?? 0,
        'AddColumnModel.RowId': el.dataset.rowId,
    })   
}

async function onAddPlugin(el) {
    console.log('should open plugins sidebar')

}

const actions = {
    'add-section': onAddSection,
    'add-column': onAddColumn,
    'add-plugin': onAddPlugin
    // 'cancel-edit-mode'() {
    //     actions["hide-sidebar"]()
    //     window.location.href = window.location.href.replace('?pageEdit=true', '')
    // },
    // 'save-edit-mode'() {
    //     // actions["hide-sidebar"]()
    //     save()
    // },
    // 'responsive-mobile'() {
    //     updateResponsive('mobile')
    // },
    // 'responsive-tablet'() {
    //     updateResponsive('tablet')
    // },
    // 'responsive-laptop'() {
    //     updateResponsive('laptop')
    // },
    // 'responsive-large'() {
    //     updateResponsive('large')
    // },
    // 'plugin-container-action-delete'(el) {
    //     const id = el.parentElement.parentElement.dataset.id

    //     if(id == '00000000-0000-0000-0000-000000000000') {
    //         el.parentElement.parentElement.remove()
    //     } else {
    //         el.parentElement.parentElement.dataset.deleted = true
    //         el.parentElement.parentElement.classList.add('f-hidden')
    //     }
    // },
    // 'show-sidebar'() {
    //     pageEditorElement.classList.remove('f-page-editor-sidebar-close')
    //     pageEditorElement.classList.add('f-page-editor-sidebar-open')
    //     setTimeout(() => {
    //         updateResizerPosition()
    //     }, 300)
    // },
    // 'hide-sidebar'() {
    //     pageEditorElement.classList.add('f-page-editor-sidebar-close')
    //     pageEditorElement.classList.remove('f-page-editor-sidebar-open')
    //     setTimeout(() => {
    //         updateResizerPosition()
    //     }, 300)
    // }
}

function initializeActions(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        action.addEventListener('click', () => {
            actions[action.dataset.action](action)
        })
    })
    initPluginActions(element)
}

export async function onInit() {
    console.log('onInit')
    const frameDocument = await getFrameDocument()

    // initializeResponsive()

    initializeActions(frameDocument)
    initializeActions(document)
    initializeSortable(frameDocument)
    initializeColumns()
   
    initPluginActions(frameDocument)

    frameDocument.body.classList.add('f-edit-content')
}
