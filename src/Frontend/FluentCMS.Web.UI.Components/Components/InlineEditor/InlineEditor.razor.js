async function onSave(ev) {
    const dotnet = ev.target.closest('.f-inline-editor').dotnet
    let content = ev.target.closest('.f-inline-editor').querySelector('.f-inline-editor-content').innerHTML   

    // Remove dynamically added contenteditable attributes.
    content = content.replace(/ contenteditable=""/g, '')

    // remove ="" of simple attributes. disabled="" readonly=""...
    content = content.replace(/=""/g, '')
    
    // These comments are added by blazor in runtime.
    content = content.replace(/<!--!-->/g, '')

    await dotnet.invokeMethodAsync('Save', content)
}

async function onCancel(ev) {
    const dotnet = ev.target.closest('.f-inline-editor').dotnet

    await dotnet.invokeMethodAsync('Cancel')
}

function onInput(ev) {
    const element = ev.target.closest('.f-inline-editor')
    element.classList.add('f-inline-editor-dirty')
}

export function reinitialize(dotnet, element, content) {
    element.classList.remove('f-inline-editor-dirty')

    dispose(dotnet, element)
    initialize(dotnet, element, content)
} 

export async function initialize(dotnet, element, content) {
    element.dotnet = dotnet

    const saveButton = element.querySelector('.f-inline-editor-action-save')
    const cancelButton = element.querySelector('.f-inline-editor-action-cancel')

    saveButton.addEventListener('click', onSave)
    cancelButton.addEventListener('click', onCancel)
    
    element.querySelector('.f-inline-editor-content').innerHTML = content
    
    setTimeout(() => {
        const inlineElements = element.querySelectorAll('[data-inline-editable]')
        for(let el of inlineElements) {
            el.setAttribute('contenteditable', '')        
            el.addEventListener('input', onInput)
        }
    })
}

export async function dispose(dotnet, element) {
    if(!element) return;
    const saveButton = element.querySelector('.f-inline-editor-action-save')
    const cancelButton = element.querySelector('.f-inline-editor-action-cancel')
    
    saveButton.removeEventListener('click', onSave)
    cancelButton.removeEventListener('click', onCancel)

    const inlineElements = element.querySelectorAll('[data-inline-editable]')
    for (let el of inlineElements) {
        el.removeEventListener('input', onInput)
        el.removeAttribute('contenteditable')
    }
}
