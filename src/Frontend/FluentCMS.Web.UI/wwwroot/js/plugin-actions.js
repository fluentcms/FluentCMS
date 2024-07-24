import { closeOffcanvas, openOffcanvas } from "./helpers.js"

function getUrl(pluginId, mode, itemId) {
    let url = window.location.origin  + window.location.pathname + `?pluginId=${pluginId}&viewName=${mode}`
    if(itemId) {
        url += '&id=' + itemId
    }
    return url
}
export async function onPluginEdit(el, callback) {
    console.log('onPluginEdit')
    // frameDocument.querySelectorAll('[data-plugin-item-action]').forEach(item => {
        // item.addEventListener('click', () => {
            // const mode = item.getAttribute('data-plugin-item-action');

    const mode = el.dataset.pluginItemAction
    const pluginId = el.dataset.pluginId
    const editPluginIframe = document.getElementById('f-edit-plugin-iframe')
    // var res = await fetch(getUrl(pluginId, mode)).then(res => res.text())

    // editPluginIframe.srcdoc = res
    editPluginIframe.setAttribute('src', getUrl(pluginId, mode));
    setTimeout(() => {
        console.log('here')
        console.log(editPluginIframe.contentDocument.querySelectorAll('form'))
        editPluginIframe.contentWindow.addEventListener('fluentcms:afterenhanced', async () => {
            closeOffcanvas()
            // reload()
            
            callback()
        })
        // editPluginIframe.contentDocument.querySelectorAll('form').forEach(x => {
        //     console.log('form', x)

        //     x.onsubmit = e => {
        //         console.log('x submitted', e)
        //         // form submitted
        //         // reload()
        //         // edit
        //     }   
        // })
    }, 1000)

    // editPluginIframe.contentWindow.addEventListener('fluentcms:closepage', () => {
    //     onCloseOffcanvas()
    // })

            // window.location.href = getUrl(pluginId, mode);
        // })
    // })
    openOffcanvas('f-offcanvas-plugin-edit', 800)    
}


// function getUrl(pluginId, mode, itemId) {
//     let url = window.location.pathname + `?pluginId=${pluginId}&viewName=${mode}`
//     if(itemId) {
//         url += '&id=' + itemId
//     }
//     return url
// }

// function openModal(id, doc) {
//     console.log('openModal', id)
//     console.log(doc.querySelector('#' + id))
//     const modal = doc.getElementById(id)
//     const modalBackdrop = doc.getElementById(id + '-backdrop')

//     modal.classList.remove('hidden')
//     modal.classList.add('flex')

//     modalBackdrop.classList.remove('hidden')
//     modalBackdrop.classList.add('flex')

//     function closeModal(event) {
//         modal.querySelectorAll(`[data-${id}-close]`).forEach(element => {
//             element.removeEventListener('click', closeModal)
//         })
//         modal.classList.add('hidden')
//         modal.classList.remove('flex')

//         modalBackdrop.classList.add('hidden')
//         modalBackdrop.classList.remove('flex')
//     }

//     modal.querySelectorAll(`[data-${id}-close]`).forEach(element => {
//         element.addEventListener('click', closeModal)
//     })
// }

// export function initPluginActions(doc) {
//     console.log('initPluginActions')
//     doc.querySelectorAll('[data-plugin-item-id]').forEach(pluginItem => {
//         const itemId = pluginItem.getAttribute('data-plugin-item-id');
//         const pluginId = pluginItem.getAttribute('data-plugin-id');

//         pluginItem.querySelectorAll('[data-plugin-item-action]').forEach(item => {
//             item.addEventListener('click', () => {
//                 const mode = item.getAttribute('data-plugin-item-action');

//                 if(mode == 'delete') {
//                     const typeName = item.getAttribute('data-plugin-item-type')
//                     doc.getElementById("f-action-delete-item-id").value = itemId
//                     doc.getElementById("f-action-delete-item-plugin-id").value = pluginId
//                     doc.getElementById("f-action-delete-item-type-name").value = typeName

//                     openModal('f-action-delete-modal', doc)

//                     return;
//                 }

//                 window.location.href = getUrl(pluginId, mode, itemId);
//             })
//         })
//     })

//     // doc.querySelectorAll('[data-plugin-item-action]').forEach(item => {
//     //     item.addEventListener('click', () => {
//     //         // const mode = item.getAttribute('data-plugin-item-action');
//     //         const mode = item.dataset.pluginItemAction
//     //         const pluginId = item.dataset.pluginId

//     //         window.location.href = getUrl(pluginId, mode);
//     //     })
//     // })
// }