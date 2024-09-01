import './sortable.js'

import {actions} from './actions.js'
import { createPlugin, updateBlockContent, updatePlugin, updatePluginCols, updatePluginOrders } from './api.js';
import {Columns} from './columns.js'

export function initColumns(frameDocument) {
    window.sections = []
    frameDocument.querySelectorAll('.f-section').forEach(section => {
        const column = new Columns(section, {
            doc: frameDocument,
            gridLines: true,
            colClass: 'f-plugin-container',
            breakpointLg: 992,
            breakpointMd: 480,
            onResize(el) {
                updatePluginCols(el, section)
            }
        })
        
        column.init()
        window.sections[section.dataset.name] = column
    })
}


export function initializeSortable(frameDocument) {
    const sectionElements = frameDocument.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            direction: 'vertical',
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag',
            onEnd() {
                updatePluginOrders()
            }
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
            const order = event.newIndex - 1
            const sectionName = event.to.dataset.name

            createPlugin({ definitionId, order, sectionName, blockId: item.dataset.blockId })
        }
    });
}

export async function hydrate(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        if(action.dataset.trigger === 'load') {
            actions[action.dataset.action](action)
        } else {
            action.addEventListener('click', () => {
                actions[action.dataset.action](action)
            })
        }
    })
}

export async function reload() {
    const html = await fetch(window.location.href).then(res => res.text())   

    const template = document.createElement('template')
    template.innerHTML = html

    setTimeout(() => {
        const node = template.content.querySelector('.f-page-editor').cloneNode(true)
        document.querySelector('.f-page-editor').innerHTML = node.innerHTML

        initializeInlineEditables(document.querySelector('.f-page-editor-iframe').contentDocument)

        initializeSortable(document.querySelector('.f-page-editor-iframe').contentDocument)
        hydrate(document.querySelector('.f-page-editor'))
    })
}

export async function reloadIframe() {
    const html = await fetch(window.location.href.replace('pageEdit', 'pagePreview')).then(res => res.text())   

    const body = html.slice(html.indexOf('<body>'), html.indexOf('</body>') + 7)

    const template = document.createElement('template')
    template.innerHTML = body

    setTimeout(() => {
        const node = template.content.cloneNode(true)
        
        const frameDocument = document.querySelector('.f-page-editor-iframe').contentDocument
        
        frameDocument.body.innerHTML = ''
        Array.from(node.childNodes).forEach(child => {
            frameDocument.body.appendChild(child);
        });  
        
        initializeInlineEditables(frameDocument)
        initializeSortable(frameDocument)
        initColumns(frameDocument)
        hydrate(frameDocument)
    })
}

export function debounce(cb, timeout = 300) {
    let timer;
    return (...args) => {
        if(timer) clearTimeout(timer)
        timer = setTimeout(() => {
            cb(...args)
        }, timeout)
    }
}
export function initializeInlineEditables(doc) {
    async function saveBlock(el) {
        let plugin = findParentPlugin(el)
        let block = findParentBlock(el)

        await updateBlockContent({pluginId: plugin.id, id: block.dataset.id, content: block.innerHTML})
    }

    const saveBlockDebounced = debounce(saveBlock, 1000)

    function findParentPlugin(el) {
        if(el.classList.contains('f-plugin-container')) {
            return el
        }
        return findParentBlock(el.parentElement)
    }
    function findParentBlock(el) {
        if(el.classList.contains('f-block')) {
            return el
        }
        return findParentBlock(el.parentElement)
    }
    

    doc.querySelectorAll('[data-inline-editable]').forEach(el => {
        el.setAttribute('contenteditable', '')
        el.addEventListener('keyup', () => {
            saveBlockDebounced(el)
            console.log('changed')
        })
        el.addEventListener('click', (ev) => {
            ev.preventDefault()
            ev.stopPropagation()
        })

        if(el.tagName === 'A') {
           
            console.log('A')
        } else {
            console.log(el.tagName)
        }
    })

}

export async function request(handler, body) {
    const token = document.querySelector('[name="__RequestVerificationToken"]').value

    const formData = new FormData()
    formData.set('__RequestVerificationToken', token)
    formData.set('_handler', handler)

    for(let key in body) {
        formData.set(key, body[key])
    }

    await fetch(window.location.href, {
        method: 'POST',
        body: formData
    })
}