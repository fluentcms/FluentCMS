const localStorageKey = "SITE_BUILDER"

const createForm = document.querySelector("#f-page-editor-form-create")
const updateForm = document.querySelector("#f-page-editor-form-update")
const deleteForm = document.querySelector("#f-page-editor-form-delete")

const actions = {
    'open-edit-mode'() {
        document.body.classList.remove('f-view-mode')
        document.body.classList.add('f-edit-mode')
        actions["show-sidebar"]()
    },
    'cancel-edit-mode'() {
        document.body.classList.remove('f-edit-mode')
        document.body.classList.add('f-view-mode')
        actions["hide-sidebar"]()
    },
    'save-edit-mode'() {
        document.body.classList.remove('f-edit-mode')
        document.body.classList.add('f-view-mode')
        actions["hide-sidebar"]()
        const sections = {}
        document.querySelectorAll('.f-section').forEach(section => {
            sections[section.dataset.name] = []

            section.querySelectorAll('.f-plugin-container').forEach(plugin => {
                sections[section.dataset.name].push(plugin.dataset.id)
            })
        })

        var updateInputs = ''
        for(let section in sections) {
            for(let index in sections[section]) {
                const plugin = sections[section][index]
                updateInputs += `
                    <input type="hidden" name="UpdateModel[${index}].Section" value="${section}" />
                    <input type="hidden" name="UpdateModel[${index}].Id" value="${plugin}" />
                    <input type="hidden" name="UpdateModel[${index}].Order" value="${index}" />
                `
            }
        }
        updateForm.querySelector('#f-update-form-inputs').outerHTML = updateInputs

        setTimeout(() => {
            submitForm(updateForm, {})
        })
    },
    'plugin-container-action-delete'(el) {
        el.parentElement.parentElement.remove()
        const id = el.parentElement.parentElement.dataset.id
        submitForm(deleteForm, {DeleteModel: id})
    },
    'show-sidebar'() {
        document.body.classList.remove('f-page-editor-sidebar-close')
        document.body.classList.add('f-page-editor-sidebar-open')
    },
    'hide-sidebar'() {
        document.body.classList.add('f-page-editor-sidebar-close')
        document.body.classList.remove('f-page-editor-sidebar-open')
    }
}

function initializeActions(element) {
    element.querySelectorAll('[data-action]').forEach(action => {
        action.addEventListener('click', () => {
            actions[action.dataset.action](action)
        })
    })
}

function submitForm(form, data) {
    for(let key in data) {
        form.querySelector(`[name="${key}"]`).value = data[key]
    }
    saveState()
    form.submit()
}

function initializeSortable() {
    const sectionElements = document.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag'
        });

        new Sortable(document.querySelector('.f-plugin-definition-list'), {
            animation: 150,
            group: {
                name: 'shared',
                pull: 'clone',
                put: false
            },
            sort: false,
            draggable: '.f-plugin-definition-item',
            onEnd(event) {
                if(event.from === event.to) return;
                const definitionId = event.clone.dataset.id
                const sectionName = event.item.parentElement.dataset.name

                submitForm(createForm, {
                    'CreateModel.DefinitionId': definitionId,
                    'CreateModel.Order': event.newIndex - 1,
                    'CreateModel.Section': sectionName
                })
            }
        });
    });
}

function reloadSortable() {
    document.querySelectorAll('.f-section').forEach(el => {
        Sortable.get(el).destroy()
    })
    Sortable.get(document.querySelector('.f-plugin-definition-list')).destroy()

    initializeSortable()
}

function onInit() {
    const state = loadState()
    if(state.mode === 'edit') {
        document.body.classList.add('f-edit-mode')
    } else {
        document.body.classList.add('f-view-mode')
    }
    if(state.sidebarOpen) {
        document.body.classList.add('f-page-editor-sidebar-open')
    } else {
        document.body.classList.add('f-page-editor-sidebar-close')
    }

    if(state.scroll) {
        scrollTo({
            top: state.scroll
        })
    }

    initializeActions(document)
    initializeSortable()
}

function saveState() {
    localStorage.setItem(localStorageKey, JSON.stringify({
        mode: document.body.classList.contains('f-view-mode') ? 'view' : 'edit',
        sidebarOpen: document.body.classList.contains('f-page-editor-sidebar-open') ? true : false,
        scroll: window.scrollY
    }))
}

function loadState() {
    return JSON.parse(localStorage.getItem(localStorageKey) ?? '{}') ?? {}
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

onInit()
