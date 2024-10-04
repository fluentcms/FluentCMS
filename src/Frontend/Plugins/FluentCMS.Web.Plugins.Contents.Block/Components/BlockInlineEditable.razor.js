import { debounce } from '/_content/FluentCMS.Web.UI.Components/js/helpers.js'

const blockMap = new Map()

function updateContent(dotnet, content) {
    content = content.replace(/ contenteditable=""/g, '')
    content = content.replace(/<!--!-->/g, '')
    dotnet.invokeMethodAsync('UpdateContent', content)

}

const updateContentDebounced = debounce(updateContent, 1000)

export async function done(dotnet, element, content) {
    element.parentElement.classList.remove('f-block-edited')

    if(content) {
        element.innerHTML = content;
    }

    setTimeout(() => {
        initialize(dotnet, element)
    })

}

export async function initialize(dotnet, element) {
    const inlineElements = element.querySelectorAll('[data-inline-editable]')

    var cleanCallbacks = new Map()

    function cleanup() {
        for(let [element, callback] of cleanCallbacks.entries()) {
            element.removeEventListener('input', callback)
        }
    }
    
    for(let el of inlineElements) {
        el.setAttribute('contenteditable', '')

        function onInput(ev) {
            element.parentElement.classList.add('f-block-edited')
            updateContentDebounced(dotnet, element.innerHTML)
        }

        el.addEventListener('input', onInput)
        cleanCallbacks.set(el, onInput)
    }
    blockMap.set(element, {cleanup})
}

export async function dispose(dotnet, element) {
    const {cleanup} = blockMap.get(element)
    cleanup()
    blockMap.delete(element)
}