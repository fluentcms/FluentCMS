import { updateResizerPosition, updateResponsive } from "./responsive.js"

export const actions = {
    'done'() {
        actions["hide-sidebar"]()
        window.location.href = window.location.href.replace('?pageEdit=true', '')
    },
    'responsive-default'() {
        updateResponsive('default')
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
    'open-plugin-view'(el) {
        const viewName = el.dataset.viewName
        const pluginId = el.dataset.pluginId
        const id = el.dataset.id

        let url = window.location.pathname

        if(pluginId) url += '?pluginId=' + pluginId;
        if(viewName) url += '&viewName=' + viewName;
        if(id) url += '&id=' + id;
        url += '&redirectTo=' + encodeURIComponent(window.location.href);

        window.location.href = url
    },
    'add-plugin'(el) {
        updateResponsive('default')
        const sidebar = document.querySelector('.f-page-editor-iframe').contentDocument.querySelector('.f-page-editor-sidebar')
        sidebar.classList.toggle('f-page-editor-sidebar-open')
    },
    'exit-design'(el) {
        window.location.href = window.location.href.replace('?pageEdit=true', "")
    }
}

