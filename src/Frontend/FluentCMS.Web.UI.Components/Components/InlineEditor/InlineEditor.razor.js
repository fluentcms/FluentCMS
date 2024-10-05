import { debounce } from '/_content/FluentCMS.Web.UI.Components/js/helpers.js'

const updateContentDebounced = debounce(updateContent, 1000)

function updateContent(dotnet, content) {
    console.log('updateContent')
    content = content.replace(/ contenteditable=""/g, '')
    dotnet.invokeMethodAsync('UpdateContent', content)
}

function onInput(ev) {
    console.log('onInput', ev)
    const element = ev.target.closest('.f-inline-editor-content')
    updateContentDebounced(ev.target.dotnet, element.innerHTML)
}

export function reinitialize(dotnet, element) {
    dispose(dotnet, element)
    initialize(dotnet, element)
} 

export async function initialize(dotnet, element) {
    console.log('initialize', element)
    const inlineElements = element.querySelectorAll('[data-inline-editable]')
        
    for(let el of inlineElements) {
        el.dotnet = dotnet
        el.setAttribute('contenteditable', '')        
        el.addEventListener('input', onInput)
    }
}

export async function dispose(dotnet, element) {
    console.log('dispose', element)
    const inlineElements = element.querySelectorAll('[data-inline-editable]')
    for (let el of inlineElements) {
        element.removeEventListener('input', onInput)
        el.removeAttribute('contenteditable')
    }
}
