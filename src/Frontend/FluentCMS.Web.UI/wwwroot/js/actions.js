import { updateResponsive } from "./responsive.js"

export const actions = {
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
    'add-plugin'(el) {
        updateResponsive('default')
        const sidebar = document.querySelector('.f-page-editor-iframe').contentDocument.querySelector('.f-page-editor-sidebar')
        sidebar.classList.toggle('f-page-editor-sidebar-open')
    },
    'exit-design'(el) {
        window.location.href = window.location.href.replace('?pageEdit=true', "")
    }
}

