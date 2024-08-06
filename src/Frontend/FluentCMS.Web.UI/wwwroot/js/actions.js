export const actions = {
    'cancel-edit-mode'() {
        actions["hide-sidebar"]()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
    },
    'save-edit-mode'() {
        actions["hide-sidebar"]()
        save()
    },
    'responsive-mobile'() {
        updateResponsive('mobile')
    },
    'responsive-tablet'() {
        updateResponsive('tablet')
    },
    'responsive-desktop'() {
        updateResponsive('desktop')
    },
    'plugin-container-action-delete'(el) {
        const id = el.parentElement.parentElement.dataset.id

        if(id == '00000000-0000-0000-0000-000000000000') {
            el.parentElement.parentElement.parentElement.remove()
        } else {
            el.parentElement.parentElement.parentElement.dataset.deleted = true
            el.parentElement.parentElement.parentElement.classList.add('f-hidden')
        }
    },
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
    },
    'move-to-toolbar'(el) {
        const pluginId = el.dataset.pluginId

        const toolbar = document.querySelector('.f-page-editor-iframe').contentDocument.querySelector(`[data-id="${pluginId}"] .f-plugin-toolbar-actions`)

        setTimeout(() => {
            for(let child of el.childNodes) {
                toolbar.prepend(child)
                
            }
        })
    },
    'open-plugin-view'(el) {
        const viewName = el.dataset.viewName
        const pluginId = el.dataset.pluginId
        const id = el.dataset.id

        let url = window.location.pathname

        if(pluginId) url += '?pluginId=' + pluginId;
        if(viewName) url += '&viewName=' + viewName;
        if(id) url += '&id=' + id;

        window.location.href = url
    }
}

