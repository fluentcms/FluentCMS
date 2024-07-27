import { updateResponsive, updateResizerPosition } from "./responsive.js"
import Sortable from "./sortable.js"
import { ActionManager } from "./actions.js"
import { getFrameDocument, onHideSidebar, parseStyles } from "./helpers.js"
import { onCloseOffcanvas, onPageSettings, onPagesList } from "./toolbar-actions.js"
import { onPluginEdit } from "./plugin-actions.js"
import { lifecycle } from "./lifecycle.js"
import { request } from "./request.js"
import { StyleManager } from "./styles.js"

let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')
const resizerElement = document.querySelector('.f-page-editor-iframe-resizer')

let frameDocument;

let styleManager
let actionManager;
let frameActionManager;

function initializeSortable(frameDocument) {
    frameDocument.querySelectorAll('.f-section-root').forEach(root => {
        Sortable.get(root)?.destroy()
        new Sortable(root, {
            animation: 150,
            draggable: '.f-section',
            handle: '.f-actions',
            onEnd(event) {
                saveSections()
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
            handle: '.f-actions',
            onEnd(event) {
                saveColumns()
            }
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
            handle: '.f-actions',
            onEnd(event) {
                savePlugins()
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
        'PluginUpdateModel.Id': id,
        'PluginUpdateModel.ColumnId': columnId,
        'PluginUpdateModel.Order': index,
    }

    await request({
        handler: 'PluginUpdateForm',
        body
    })
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

async function createPlugin({definitionId, columnId, index, item}) {
    await request({
        handler: 'PluginCreateForm',
        body: {
            'PluginCreateModel.DefinitionId': definitionId,
            'PluginCreateModel.ColumnId': columnId,
            'PluginCreateModel.Order': index,
        },
    })
    await savePlugins()
}

async function saveColumns() {
    let columns = {}
    iframeElement.contentDocument.querySelectorAll('.f-section').forEach(section => {
        let colIndex = 0 
        section.querySelectorAll('.f-column').forEach(el => {
            const key = el.id.replace('column-', '')
            columns[key] = {}
            columns[key].dataset = el.dataset
            columns[key].order = colIndex++;
            columns[key].sectionId = section.id.replace('section-', '');
            columns[key].style = el.getAttribute('style') ?? '';
        })
    
    })

    let requestBody = {
        
    }

    let index = 0;
    for(let id in columns) {
        const column = columns[id]
        requestBody[`ColumnUpdateModel.Columns[${index}].Id`] = id
        requestBody[`ColumnUpdateModel.Columns[${index}].Order`] = column.order
        requestBody[`ColumnUpdateModel.Columns[${index}].SectionId`] = column.sectionId
        const styles = parseStyles(column.style, column.dataset)

        for(let key in styles) {
            requestBody[`ColumnUpdateModel.Columns[${index}].Styles[${key}]`] = styles[key] 
        }

        index++;
    }

    await request({
        handler: "ColumnUpdateForm",
        body: requestBody
    })
}


async function savePlugins() {
    let plugins = {}
    iframeElement.contentDocument.querySelectorAll('.f-column').forEach(column => {
        let colIndex = 0 
        column.querySelectorAll('.f-plugin-container-content').forEach(el => {
            const key = el.id.replace('plugin-', '')
            plugins[key] = {}
            plugins[key].dataset = el.dataset
            plugins[key].order = colIndex++;
            plugins[key].columnId = column.id.replace('column-', '');
            plugins[key].style = el.getAttribute('style') ?? '';
        })
    
    })

    let requestBody = {
        
    }

    let index = 0;
    for(let id in plugins) {
        const plugin = plugins[id]
        requestBody[`PluginsUpdateModel.Pluginss[${index}].Id`] = id
        requestBody[`PluginsUpdateModel.Pluginss[${index}].Order`] = plugin.order
        requestBody[`PluginsUpdateModel.Pluginss[${index}].ColumnId`] = plugin.columnId
        const styles = parseStyles(plugin.style, plugin.dataset)

        for(let key in styles) {
            requestBody[`PluginsUpdateModel.Plugins[${index}].Styles[${key}]`] = styles[key] 
        }

        index++;
    }

    await request({
        handler: "PluginsUpdateForm",
        body: requestBody
    })
}

// savePlugins
function initializeColumns() {
    iframeElement.contentWindow.initializeColumns({
        onResize: () => {
            saveColumns()
        }
    })
}

async function onAddSection(el) {
    await request({
        handler: "AddSectionForm",
        body: {
            'AddSectionModel.Submitted': true,
            'AddSectionModel.Order': el.dataset.order ?? 0
        },
        reload: false
    })
    
    await saveSections()
}

async function saveSections() {
    const body = {}
    let sections = []

    let index = 0;
    iframeElement.contentDocument.querySelectorAll('.f-section').forEach(el => {
        sections.push({
            id: el.id.replace('section-', ''),
            index: index++,
            style: el.getAttribute('style'),
            dataset: el.dataset,
        })
    })

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
        handler: "SectionsUpdateForm",
        body
    })
}

async function onAddColumn(el) {
    await request({
        handler: "AddColumnForm",
        body: {
            'AddColumnModel.Submitted': true,
            'AddColumnModel.Mode': el.dataset.mode,
            'AddColumnModel.Order': el.dataset.order ?? 0,
            'AddColumnModel.SectionId': el.dataset.sectionId,
        },
        reload: false
    })
    await saveColumns()
}

async function onDeleteSection(el) {
    const id = el.dataset.id
    await request({
        handler: "SectionDeleteForm",
        body: {

            'SectionDeleteModel.Submitted': true,
            'SectionDeleteModel.Id': id,
        },
        reload: false
    }) 
    await saveSections()
}

async function onDeleteColumn(el) {
    const id = el.dataset.id
    await request({
        handler: "ColumnDeleteForm",
        body: {
            'ColumnDeleteModel.Submitted': true,
            'ColumnDeleteModel.Id': id,
        },
        reload: false
    })
    await saveColumns()
}

function disableResponsiveButtons() {
    console.log('Should disable responsive buttons')
}

function showStyleEditor(el, type, id) {
    document.getElementById('styles-sidebar').classList.remove('f-hidden')
    document.getElementById('plugins-sidebar').classList.add('f-hidden')
    document.getElementById('permissions-sidebar').classList.add('f-hidden')

    styleManager.open(el)
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
    } else if(mode === 'plugin-permissions') {
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
    showSidebar({title: 'Edit Plugin', mode: 'plugin-styles', id: el.dataset.id, el})
}

function onEditColumnButtonClicked(el) {
    showSidebar({title: 'Edit Column', mode: 'column-styles', id: el.dataset.id, el})

}
function onEditSectionButtonClicked(el) {
    showSidebar({title: 'Edit Section', mode: 'section-styles', id: el.dataset.id, el})

}

function onToggleFullWidth(el) {
    const section = frameDocument.querySelector('#section-' + el.dataset.id)

    if(section.dataset.fullWidth == 'true') {
        section.dataset['fullWidth'] = 'false'
    } else {
        section.dataset['fullWidth'] = 'true'
    }

    saveSections()
}

function onEditPluginPermissionsButtonClicked(el) {
    showSidebar({title: 'Plugin Permissions', mode: 'plugin-permissions', id: el.dataset.id})
}
function onEditPluginStylesButtonClicked(el) {
    showSidebar({title: 'Plugin Styles', mode: 'plugin-styles', id: el.dataset.id})
}

async function onDeletePlugin(el) {
    const id = el.dataset.id
    await request({
        handler: 'PluginDeleteForm',
        body: {
            'PluginDeleteModel.Submitted': true,
            'PluginDeleteModel.Id': id
        }
    })
} 

function onAddStyleItem(el) {
    var styleActionsEl = document.getElementById('f-style-editor-body-actions')
    var element = document.createElement('div')
    styleActionsEl.parentElement.insertBefore(element, styleActionsEl)

    element.outerHTML = getStyleEditorRow('', '')
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
    'edit-plugin-permissions': onEditPluginPermissionsButtonClicked,
    'edit-plugin-styles': onEditPluginStylesButtonClicked,
    'back'() {
        onHideSidebar()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
    },
    'pages-list': onPagesList,
    'page-settings': onPageSettings,
    'plugin-edit': onPluginEdit,
    'close-offcanvas': onCloseOffcanvas,
    'add-style-item': onAddStyleItem,
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
    'hide-sidebar': onHideSidebar
}

function initializeActions() {
    for(let key in actions) {
        frameActionManager.register(key, actions[key])
        actionManager.register(key, actions[key])
    }
}

function initializeStyleManager() {
    styleManager = StyleManager({
        frameDocument,
        async onChange(el) {
            if(el.id.startsWith('section-')) {
                await saveSections()
            }
            else if(el.id.startsWith('column-')) {
                await saveColumns()
            } 
            else {
                await savePlugins()
            } 
        }
    })
    
}

export async function onInit() {
    frameDocument = await getFrameDocument()

    frameActionManager = ActionManager(frameDocument)
    actionManager = ActionManager(document)

    // initializeResponsive()
    lifecycle.on('afterLoad', () => {
        initializeColumns()
        frameActionManager.init()
        initializeSortable(iframeElement.contentDocument)
        iframeElement.contentDocument.body.classList.add('f-edit-content')
    })

    lifecycle.on('reload', async () => {
        const url = window.location.href
        const response = await fetch(url.replace('pageEdit', 'pagePreview')).then(res => res.text())
        const body = response.match(/<body[^>]*>([\s\S]*?)<\/body>/)
        iframeElement.contentDocument.body.innerHTML = body[1]

        setTimeout(() => {
            lifecycle.trigger('afterLoad')        
        })
    })

    initializeStyleManager()
    initializeActions()
    actionManager.init()

    lifecycle.trigger('afterLoad')
}
