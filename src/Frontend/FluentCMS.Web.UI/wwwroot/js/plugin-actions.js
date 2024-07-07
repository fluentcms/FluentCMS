
function getUrl(pluginId, mode, itemId) {
    let url = window.location.pathname + `?pluginId=${pluginId}&viewName=${mode}`
    if(itemId) {
        url += '&id=' + itemId
    }
    return url
}

function openModal(id) {
    const modal = document.getElementById(id)
    const modalBackdrop = document.getElementById(id + '-backdrop')

    modal.classList.remove('hidden')
    modal.classList.add('flex')

    modalBackdrop.classList.remove('hidden')
    modalBackdrop.classList.add('flex')

    function closeModal(event) {
        modal.querySelectorAll(`[data-${id}-close]`).forEach(element => {
            element.removeEventListener('click', closeModal)
        })
        modal.classList.add('hidden')
        modal.classList.remove('flex')

        modalBackdrop.classList.add('hidden')
        modalBackdrop.classList.remove('flex')
    }

    modal.querySelectorAll(`[data-${id}-close]`).forEach(element => {
        element.addEventListener('click', closeModal)
    })
}

window.initPluginActions = function (element) {
    element.querySelectorAll('[data-plugin-item-id]').forEach(pluginItem => {
        const itemId = pluginItem.getAttribute('data-plugin-item-id');
        const pluginId = pluginItem.getAttribute('data-plugin-id');

        pluginItem.querySelectorAll('[data-plugin-item-action]').forEach(item => {
            item.addEventListener('click', () => {
                const mode = item.getAttribute('data-plugin-item-action');

                if(mode == 'delete') {
                    const typeName = item.getAttribute('data-plugin-item-type')
                    document.getElementById("f-action-delete-item-id").value = itemId
                    document.getElementById("f-action-delete-item-plugin-id").value = pluginId
                    document.getElementById("f-action-delete-item-type-name").value = typeName

                    openModal('f-action-delete-modal')

                    return;
                }

                window.location.href = getUrl(pluginId, mode, itemId);
            })
        })
    })

    element.querySelectorAll('[data-plugin-items-root]').forEach(pluginItem => {
        const pluginId = pluginItem.getAttribute('data-plugin-id');

        pluginItem.querySelectorAll('[data-plugin-item-action]').forEach(item => {
            item.addEventListener('click', () => {
                const mode = item.getAttribute('data-plugin-item-action');

                window.location.href = getUrl(pluginId, mode);
            })
        })
    })
}
