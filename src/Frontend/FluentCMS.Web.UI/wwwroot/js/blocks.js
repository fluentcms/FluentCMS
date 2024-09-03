import { debounce } from "../helpers.js"
import { updateBlockContent } from "./api.js"

function getBlockContent(blockEl) {
    blockEl.querySelectorAll('[data-inline-editable]').forEach(el => el.removeAttribute('contenteditable'))
    return blockEl.innerHTML.replace(/data-inline-editable=\"\"/g, 'data-inline-editable')
}

async function saveBlock(el) {
    let plugin = findParentPlugin(el)
    let block = findParentBlock(el)

    await updateBlockContent({
        pluginId: plugin.dataset.id, 
        id: block.dataset.id, 
        content: getBlockContent(block)
    })
}

function findParentPlugin(el) {
    return el.closest('.f-plugin-container');
}

function findParentBlock(el) {
    return el.closest('.f-block');
}

export function initializeInlineEditables(doc) {   
    console.log('initialize inline edit')
    const saveBlockDebounced = debounce(saveBlock, 1000)
    
    doc.querySelectorAll('[data-inline-editable]').forEach(el => {
        el.setAttribute('contenteditable', '')

        el.addEventListener('input', () => {
            saveBlockDebounced(el)
        })

        el.addEventListener('click', (ev) => {
            ev.preventDefault()
            ev.stopPropagation()
        })
    })
}