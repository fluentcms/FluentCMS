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
    'toggle-add-plugin'(el) {
        updateResponsive('default')
        const sidebar = document.querySelector('.f-page-editor-iframe').contentDocument.querySelector('.f-page-editor-sidebar')
        sidebar.classList.toggle('f-page-editor-sidebar-open')

        const currentButton = document.querySelector('.f-toolbar-button.f-button-visible')
        const nextButton = document.querySelector('.f-toolbar-button.f-button-hidden')

        nextButton.classList.add('f-button-visible')
        nextButton.classList.remove('f-button-hidden')
        currentButton.classList.add('f-button-hidden')
        currentButton.classList.remove('f-button-visible')
    },
    'exit-design'(el) {
        window.location.href = window.location.href.replace('?pageEdit=true', "")
    }
}

export async function initializeClickEvents(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        action.addEventListener('click', () => {
            actions[action.dataset.action](action)
        })
    })
}