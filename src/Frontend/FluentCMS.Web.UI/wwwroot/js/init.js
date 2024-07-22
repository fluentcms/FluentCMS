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

// let updateForm = document.querySelector("#f-page-editor-form-update")

// function save() {
//     const sections = {}

//     frameDocument.querySelectorAll('.f-section').forEach(section => {
//         sections[section.dataset.name] = []

//         section.querySelectorAll('.f-plugin-container').forEach(plugin => {
//             sections[section.dataset.name].push(plugin.dataset)
//         })
//     })

//     var updateInputs = '<input type="hidden" name="Model.Submitted" value="true" />'
//     let deleteIndex = 0;
//     let newPluginsIndex = 0;
//     let updatedPluginsIndex = 0;
//     let pluginOrder = 0;
//     for(let section in sections) {
//         for(let index in sections[section]) {
//             const plugin = sections[section][index].id
//             const cols = sections[section][index].cols
//             const colsMd = sections[section][index].colsMd
//             const colsLg = sections[section][index].colsLg
//             const definitionId = sections[section][index].definitionId
//             const deleted = sections[section][index].deleted

//             if(deleted) {
//                 updateInputs += `
//                     <input type="hidden" name="Model.DeleteIds[${deleteIndex}]" value="${plugin}" />
//                 `
//                 deleteIndex++;
//             }
//             else if(plugin == '00000000-0000-0000-0000-000000000000') {
//                 // PageId will be filled in backend
//                 updateInputs += `
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].PageId" value="00000000-0000-0000-0000-000000000000" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].DefinitionId" value="${definitionId}" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Order" value="${pluginOrder}" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Cols" value="${cols}" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsMd" value="${colsMd}" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsLg" value="${colsLg}" />
//                     <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Section" value="${section}" />
//                 `
//                 pluginOrder++;
//                 newPluginsIndex++;
//             }
//             else {
//                 updateInputs += `
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Section" value="${section}" />
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Id" value="${plugin}" />
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Order" value="${pluginOrder}" />
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Cols" value="${cols}" />
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsMd" value="${colsMd}" />
//                     <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsLg" value="${colsLg}" />
//                 `
//                 pluginOrder++;
//                 updatedPluginsIndex++;
//             }
//         }
//     }

//     updateForm.querySelector('#f-update-form-inputs').outerHTML = updateInputs
//     setTimeout(() => {
//         const body = new FormData(updateForm)

//         fetch(window.location.href, {
//             method: 'POST',
//             body
//         })
//     })
// }

// function submitForm(form, data) {
//     for(let key in data) {
//         form.querySelector(`[name="${key}"]`).value = data[key]
//     }
//     form.submit()
// }

function initializeSortable(frameDocument) {
    frameDocument.querySelectorAll('.f-section-root').forEach(root => {
        Sortable.get(root)?.destroy()
        new Sortable(root, {
            animation: 150,
            draggable: '.f-section',
            handle: '.f-action-draggable',
            onEnd(event) {
                saveSectionsOrder()
            }
        });
    })

    const rowElements = frameDocument.querySelectorAll('.f-row');

    rowElements.forEach(row => {
        Sortable.get(row)?.destroy()

        new Sortable(row, {
            animation: 150,
            group: 'shared-row',
            draggable: '.f-column',
            // ghostClass: 'f-plugin-container-moving',
            // chosenClass: 'f-plugin-container-chosen',
            handle: '.f-action-draggable',
            onEnd(event) {
                // const rowId = event.to.id.replace('row-', '');
                // const id = event.item.id.replace('column-', '')
                saveColumns()
                // updatePlugin({ id, index: event.newIndex, rowId})
            }
            // onEnd(event) {
            //     // const columnId = event.to.id.replace('column-', '');
            //     // const id = event.item.dataset.id

            // }
        });
    });

    const columnElements = frameDocument.querySelectorAll('.f-column');

    columnElements.forEach(column => {
        Sortable.get(column)?.destroy()

        new Sortable(column, {
            animation: 150,
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            // handle: '.f-plugin-container-action-drag',
            handle: '.f-action-draggable',
            onEnd(event) {

                const columnId = event.to.id.replace('column-', '');
                const id = event.item.id.replace('plugin-', '')

                updatePlugin({ id, index: event.newIndex,  columnId})

            }
        });
    });

    const pluginDefinitions = document.querySelector('.f-plugin-definition-list')
    Sortable.get(pluginDefinitions)?.destroy()

    new Sortable(pluginDefinitions, {
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
            const columnId = event.to.id.replace('column-', '')

            createPlugin({ definitionId, item, index: event.newIndex, columnId })
        }
    });
}

async function updatePlugin({id, columnId, index}) {
    const body = {
        '_handler': 'PluginUpdateForm',
        'PluginUpdateModel.Id': id,
        'PluginUpdateModel.ColumnId': columnId,
        'PluginUpdateModel.Order': index,
    }

    
    await request(body)

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

function createPlugin({definitionId, columnId, index, item}) {
    // item.classList.remove('f-plugin-definition-item')
    // item.classList.add('f-plugin-container')
    // const cols = 12
    // const colsMd = 0
    // const colsLg = 0

    request({
        '_handler': 'PluginCreateForm',
        'PluginCreateModel.DefinitionId': definitionId,
        'PluginCreateModel.ColumnId': columnId,
        'PluginCreateModel.Order': index,
    })


    // const pageId = '00000000-0000-0000-0000-000000000000';

    // setTimeout(() => {
    //     // _handler: UpdatePluginForm
    //     // __RequestVerificationToken: CfDJ8PuVbaR1sHBOvuLydAdhPdS6nFFhM7eu29IOqf-5F1JRXcEXCWrMS7nUql8VqVNT4ctfGixG1DwfugOxVUgVPp6nEculRq8apOwX66kkwTmzoQ9_GQld_t89NFl1QQ5-W_EfAun8bDQLQNCWQ5_d-njmF1yG0yZw_Qk7iMJbyJGR7y59tE9_vUSG2Y9nC4NJwg

    //     const requestBody = {
    //         '_handler': 'UpdatePluginForm',
    //         '__RequestVerificationToken': document.querySelector('[name="__RequestVerificationToken"]').value,
    //         'Model.CreatePlugins[0].PageId': pageId,
    //         'Model.CreatePlugins[0].DefinitionId': definitionId,
    //         'Model.CreatePlugins[0].Order': index - 1,
    //         'Model.CreatePlugins[0].Cols': cols,
    //         'Model.CreatePlugins[0].ColsMd': colsMd,
    //         'Model.CreatePlugins[0].ColsLg': colsLg,
    //         'Model.CreatePlugins[0].Section': sectionName,
    //         "Model.Submitted": true,
    //         'Model.UpdatePlugins': '[]', // TODO
    //         'Model.DeleteIds': '[]', // TODO
    //     }

    //     const body = new FormData()
    //     for(let key in requestBody) {
    //         body.append(key, requestBody[key])
    //         // body.set('Model.DeleteIds', [])
    //     }

    //     fetch(window.location.href, {
    //         method: 'POST',
    //         body
    //     }).then(async res => {
            
    //         iframeElement.contentWindow.location.reload()
    //         // const html = await fetch(window.location.href).then(res => res.text())
    //         // document.documentElement.innerHTML = html
    //         setTimeout(() => {
    //             onInit()
    //         })
    //     })
    // })
}

function parseStyles(styleStr, dataset) {
    console.log('parseStyles', styleStr, dataset)
    let object = {}
    let stylesSplitted = styleStr.split(';').filter(Boolean);

    for(let style of stylesSplitted) {
        const [key, value] = style.split(':').map(x => x.trim());
        object[key] = value
    }

    for(let key in dataset) {
        object[key[0].toUpperCase() + key.slice(1)] = dataset[key]
    }

    return object
}

async function saveColumns() {
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
    })

    const response = await fetch(url.replace('pageEdit', 'pagePreview')).then(res => res.text())
    iframeElement.contentDocument.documentElement.innerHTML = response
    setTimeout(() => {
        initializeColumns()
        // initializeActions(document)
        initializeActions(frameDocument)
        initializeSortable(frameDocument)
        initPluginActions(frameDocument)
    
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
    await request({
        '_handler': "AddSectionForm",
        'AddSectionModel.Submitted': true,
        'AddSectionModel.Order': el.dataset.order ?? 0
    })
    
    // update order
    await saveSectionsOrder()
}

async function saveSectionsOrder() {
    const body = {}
    let sections = []

    let index = 0;
    iframeElement.contentDocument.querySelectorAll('.f-section').forEach(el => {
        // el.dataset.order = index++
        sections.push({
            id: el.id.replace('section-', ''),
            index: index++,
            style: el.getAttribute('style'),
            dataset: el.dataset,
        })
    })
    console.log(sections)

    for(let section of sections)
    {
        body[`SectionsUpdateModel.Sections[${section.index}].Id`] = section.id
        body[`SectionsUpdateModel.Sections[${section.index}].Order`] = section.index

        const styles = parseStyles(section.style, section.dataset)

        for(let key in styles) {
            body[`SectionsUpdateModel.Sections[${section.index}].Styles[${key}]`] = styles[key] 
        }
    }
    await request({
        '_handler': "SectionsUpdateForm",
        ...body
    })
}


// async function saveOrders() {
//     saveSectionsOrder()
//     alert('save columns order')
//     alert('save rows order')
//     alert('save plugins order')
//     // await newFunction()

//     // async function newFunction() {
//     //     const body = {}
//     //     let sections = []

//     //     let index = 0
//     //     iframeElement.contentDocument.querySelectorAll('.f-section').forEach(el => {
//     //         // el.dataset.order = index++
//     //         sections.push({
//     //             id: el.id.replace('section-', ''),
//     //             index: index++,
//     //             style: el.getAttribute('style'),
//     //             dataset: el.dataset,
//     //         })
//     //     })

//     //     for (let section of sections) {
//     //         body[`SectionsUpdateModel.Sections[${section.index}].Id`] = section.id
//     //         body[`SectionsUpdateModel.Sections[${section.index}].Order`] = section.index

//     //         const styles = parseStyles(section.style, section.dataset)

//     //         for (let key in styles) {
//     //             requestBody[`SectionsUpdateModel.Sections[${section.index}].Styles[${key}]`] = styles[key]
//     //         }
//     //     }
//     //     await request({
//     //         '_handler': "SectionsUpdateForm",
//     //         ...body
//     //     })
//     // }
// }


async function onAddColumn(el) {
    await request({
        '_handler': "AddColumnForm",
        'AddColumnModel.Submitted': true,
        'AddColumnModel.Mode': el.dataset.mode,
        'AddColumnModel.Order': el.dataset.order ?? 0,
        'AddColumnModel.RowId': el.dataset.rowId,
    })   
}

async function onDeleteSection(el) {
    const id = el.dataset.id
    await request({
        '_handler': "SectionDeleteForm",
        'SectionDeleteModel.Submitted': true,
        'SectionDeleteModel.Id': id,
    }) 
    await saveSectionsOrder()
}

async function onDeleteColumn(el) {
    const id = el.dataset.id
    await request({
        '_handler': "ColumnDeleteForm",
        'ColumnDeleteModel.Submitted': true,
        'ColumnDeleteModel.Id': id,
    }) 
    // await saveSectionsOrder()
}

function disableResponsiveButtons() {
    console.log('Should disable responsive buttons')
}

function getStyleEditorRow(key, value) {
    const template = `<tr data-type="style">
        <td><div><input data-type="key" placeholder="key" value="$0" /></div></td>
        <td><div><input data-type="value" placeholder="value" value="$1" /></div></td>
        <td>
            <button class="f-style-editor-remove-btn"></button>
        </td>
    </tr>`

    return template.replace('$0', key).replace('$1', value)
}

function showStyleEditor(el, type, id) {
    document.querySelectorAll('[data-type="style"]').forEach(el => {
        el.parentNode.removeChild(el)
    })

    document.getElementById('styles-sidebar').classList.remove('f-hidden')
    document.getElementById('plugins-sidebar').classList.add('f-hidden')
    document.getElementById('permissions-sidebar').classList.add('f-hidden')

    document.getElementById('styles-sidebar').dataset.type = type;
    document.getElementById('styles-sidebar').dataset.id = id;
   
    let result = ''

    let parsedStyles = parseStyles(el.getAttribute('style') ?? '', {})
    console.log(parsedStyles)
    for(let key in parsedStyles) {
        if(key) {
            result += getStyleEditorRow(key, parsedStyles[key])
        }
    }
    // document.getElementById('f-style-editor-body').outerHTML = result
    const styleActionsEl = document.getElementById('f-style-editor-body-actions')
    var element = document.createElement('div')
    styleActionsEl.parentElement.insertBefore(element, styleActionsEl)
    element.outerHTML = result

    console.log(result)
}

function showSidebar({title, mode, id, el} = {}) {
    disableResponsiveButtons()
    pageEditorElement.classList.remove('f-page-editor-sidebar-close')
    pageEditorElement.classList.add('f-page-editor-sidebar-open')

    if(title) {
        document.getElementById('f-sidebar-title').textContent = title
    }

    if(mode === 'plugins') {
        document.getElementById('plugins-sidebar').classList.remove('f-hidden')
        document.getElementById('styles-sidebar').classList.add('f-hidden')
        document.getElementById('permissions-sidebar').classList.add('f-hidden')
        
    } else if(mode === 'plugin-styles') {
        showStyleEditor(frameDocument.getElementById(`plugin-${id}`), 'plugin', id)
    } else if(mode === 'section-styles') {
        showStyleEditor(frameDocument.getElementById(`section-${id}`), 'section', id)
    } else if(mode === 'column-styles') {
        showStyleEditor(frameDocument.getElementById(`column-${id}`), 'column', id)

    } else if(mode === 'section-permissions') {
        document.getElementById('permissions-sidebar').classList.remove('f-hidden')
        document.getElementById('styles-sidebar').classList.add('f-hidden')
        document.getElementById('plugins-sidebar').classList.add('f-hidden')


        document.getElementById('permission-type').textContent = 'section'
        document.getElementById('permission-id').textContent = id

    } else if(mode === 'column-permissions') {
        document.getElementById('permissions-sidebar').classList.remove('f-hidden')
        document.getElementById('styles-sidebar').classList.add('f-hidden')
        document.getElementById('plugins-sidebar').classList.add('f-hidden')


        document.getElementById('permission-type').textContent = 'column'
        document.getElementById('permission-id').textContent = id
    }
    else if(mode === 'plugin-permissions') {
        document.getElementById('permissions-sidebar').classList.remove('f-hidden')
        document.getElementById('styles-sidebar').classList.add('f-hidden')
        document.getElementById('plugins-sidebar').classList.add('f-hidden')


        document.getElementById('permission-type').textContent = 'plugin'
        document.getElementById('permission-id').textContent = id
    }
    setTimeout(() => {
        updateResizerPosition()
    }, 300)
}

function onAddPluginButtonClicked() {
    showSidebar({title: 'Add Plugin', mode: 'plugins'})
}

function onEditPluginButtonClicked(el) {
    showSidebar({title: 'Plugin Styles', mode: 'plugin-styles', id: el.dataset.id, el})
    // document.getElementById('stlyes-table')
    

}

function onEditColumnButtonClicked(el) {
    showSidebar({title: 'Column Styles', mode: 'column-styles', id: el.dataset.id, el})

}
function onEditSectionButtonClicked(el) {
    showSidebar({title: 'Section Styles', mode: 'section-styles', id: el.dataset.id, el})

}

function onToggleFullWidth(el) {
    const section = frameDocument.querySelector('#section-' + el.dataset.id)

    if(section.dataset.fullWidth == '') {
        delete section.dataset['fullWidth']
    } else {
        section.dataset['fullWidth'] = true
    }

    saveSectionsOrder()
}

function onHideSidebar() {
    pageEditorElement.classList.add('f-page-editor-sidebar-close')
    pageEditorElement.classList.remove('f-page-editor-sidebar-open')
    setTimeout(() => {
        updateResizerPosition()
    }, 300)
}

function onEditSectionPermissionsButtonClicked(el) {
    showSidebar({title: 'Section Permissions', mode: 'section-permissions', id: el.dataset.id})
}

function onEditColumnPermissionsButtonClicked(el) {
    showSidebar({title: 'Column Permissions', mode: 'column-permissions', id: el.dataset.id})
}

function onEditPluginPermissionsButtonClicked(el) {
    showSidebar({title: 'Plugin Permissions', mode: 'plugin-permissions', id: el.dataset.id})
}

function onDeletePlugin(el) {
    const id = el.dataset.id
    alert('Should Delete plugin with id: ' + id)
} 

function onAddStyleItem(el) {
    var styleActionsEl = document.getElementById('f-style-editor-body-actions')
    var element = document.createElement('div')
    styleActionsEl.parentElement.insertBefore(element, styleActionsEl)

    element.outerHTML = getStyleEditorRow('', '')
}

function onSaveStyles() {
    const styles = {}
    document.getElementById('styles-sidebar').querySelectorAll('tbody tr[data-type="style"]').forEach(el => {
        const key = el.querySelector('input[data-type="key"]').value
        const value = el.querySelector('input[data-type="value"]').value
        styles[key] = value
    })

    const dataset = document.getElementById('styles-sidebar').dataset

    let stylesStr = ''
    for(let key in styles) {
        stylesStr += `${key}: ${styles[key]};`
    }

    let element = null
    if(dataset.type === 'section') {
        element = frameDocument.getElementById('section-' + dataset.id)
        element.setAttribute('style', stylesStr)    
        setTimeout(() => {
            saveSectionsOrder()
        })
    } else if(dataset.type === 'plugin') {
        element = frameDocument.getElementById('plugin-' + dataset.id)
        element.setAttribute('style', stylesStr)    
        // save plugin
        
    } else if(dataset.type === 'column') {
        element = frameDocument.getElementById('column-' + dataset.id)
        element.setAttribute('style', stylesStr)    
        setTimeout(() => {
            saveColumns()
        })
    }

}

const actions = {
    'add-section': onAddSection,
    'delete-section': onDeleteSection,
    'delete-column': onDeleteColumn,
    'delete-plugin': onDeletePlugin,
    'add-column': onAddColumn,
    'add-plugin': onAddPluginButtonClicked,
    'edit-plugin': onEditPluginButtonClicked,
    'edit-section': onEditSectionButtonClicked,
    'edit-column': onEditColumnButtonClicked,
    'toggle-full-width': onToggleFullWidth,
    'edit-section-permissions': onEditSectionPermissionsButtonClicked,
    'edit-column-permissions': onEditColumnPermissionsButtonClicked,
    'edit-plugin-permissions': onEditPluginPermissionsButtonClicked,
    'back'() {
        onHideSidebar()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
    },
    'add-style-item': onAddStyleItem,
    'save-styles': onSaveStyles,
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

export async function onInit() {
    const frameDocument = await getFrameDocument()

    // initializeResponsive()

    initPluginActions(frameDocument)
    initializeActions(frameDocument)
    initializeActions(document)
    initializeSortable(frameDocument)
    initializeColumns()
   
    frameDocument.body.classList.add('f-edit-content')
}
