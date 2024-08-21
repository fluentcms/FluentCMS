import { deletePlugin } from "./api.js"
import { updateResizerPosition, updateResponsive } from "./responsive.js"

export const actions = {
    'done'() {
        actions["hide-sidebar"]()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
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
    async 'plugin-container-action-delete'(el) {
        const id = el.parentElement.parentElement.parentElement.dataset.id
        // Confirm delete

        document.querySelector('.f-page-editor-delete-plugin-confirm').classList.add('open')
        document.querySelector('.f-page-editor-delete-plugin-confirm-id').value = id
        
        // await deletePlugin(id)
    },
    async 'plugin-delete-confirm-no'(el) {
        document.querySelector('.f-page-editor-delete-plugin-confirm').classList.remove('open')

    },
    async 'plugin-delete-confirm-yes'(el) {
        const id = document.querySelector('.f-page-editor-delete-plugin-confirm-id').value
        document.querySelector('.f-page-editor-delete-plugin-confirm').classList.remove('open')

        await deletePlugin(id)

        // Confirm delete
    },
    'show-sidebar'() {
        let pageEditorElement = document.querySelector('.f-page-editor')

        pageEditorElement.classList.remove('f-page-editor-sidebar-close')
        pageEditorElement.classList.add('f-page-editor-sidebar-open')
        setTimeout(() => {
            updateResizerPosition()
        }, 300)
    },
    'hide-sidebar'() {
        let pageEditorElement = document.querySelector('.f-page-editor')

        pageEditorElement.classList.add('f-page-editor-sidebar-close')
        pageEditorElement.classList.remove('f-page-editor-sidebar-open')
        setTimeout(() => {
            updateResizerPosition()
        }, 300)
    },
    'open-plugin-view'(el) {
        const viewName = el.dataset.viewName
        const pluginId = el.dataset.pluginId
        const id = el.dataset.id

        let url = window.location.pathname

        if(pluginId) url += '?pluginId=' + pluginId;
        if(viewName) url += '&viewName=' + viewName;
        if(id) url += '&id=' + id;
        url += '&redirectTo=' + encodeURIComponent(window.location.pathname + '?pageEdit=true');

        window.location.href = url
    }
}

