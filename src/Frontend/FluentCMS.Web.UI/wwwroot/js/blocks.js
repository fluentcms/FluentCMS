import { debounce } from "../helpers.js"
import { updateBlockContent } from "./api.js"
import { reloadIframe } from "./request.js"

export async function initializeBlocks() {
    const blocks = await fetch('/_content/FluentCMS.Web.UI/blocks.json').then(res => res.json())
    
    let blockDefId = document.querySelector('.f-plugin-definition-list').dataset.blockDefinitionId

    for(let block of blocks) {
        const definitionEl = document.createElement('div')
        definitionEl.classList.add('f-plugin-definition-item')
        definitionEl.dataset.id = blockDefId
        definitionEl.dataset.blockId = block.Id

        definitionEl.innerHTML = `
            <div class="f-plugin-definition-item-content">
                <span class="f-name">${block.Category} - ${block.Name}</span>
                <span class="f-description">${block.Description}</span>
            </div>
        `
        document.querySelector('.f-plugin-definition-list').appendChild(definitionEl)
    }
}

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
    await reloadIframe()
}

function findParentPlugin(el) {
    return el.closest('.f-plugin-container');
}

function findParentBlock(el) {
    return el.closest('.f-block');
}

export function initializeInlineEditables(doc) {   
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