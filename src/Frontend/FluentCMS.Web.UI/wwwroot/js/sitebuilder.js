const localStorageKey = "SITE_BUILDER"

let iframeElement = document.querySelector('.f-page-editor-iframe')

let frameDocument;
function getFrameDocument() {
    return new Promise(resolve => {
        iframeElement.onload = () => {
            frameDocument = iframeElement.contentDocument;
            resolve(frameDocument)
        }
    })
}

let createForm;
let updateForm;
let deleteForm;

function updatePlugins() {
    const sections = {}

    frameDocument.querySelectorAll('.f-section').forEach(section => {
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

    console.log(updateForm.innerHTML)
    setTimeout(() => {
        submitForm(updateForm, {})
    })
} 

const actions = {
    'cancel-edit-mode'() {
        actions["hide-sidebar"]()
        saveState(true)
        setTimeout(() => {
            window.location.href = window.location.href.replace('?pageEdit=true', '')
        })
    },
    'save-edit-mode'() {
        actions["hide-sidebar"]()
        saveState(true)
        updatePlugins()
        setTimeout(() => {
            window.location.href = window.location.href.replace('?pageEdit=true', '')
        })
    },
    'plugin-container-action-delete'(el) {
        el.parentElement.parentElement.remove()
        const id = el.parentElement.parentElement.dataset.id
        saveState()
        submitForm(deleteForm, {DeleteModel: id})
    },
    'show-sidebar'() {
        document.body.classList.remove('f-page-editor-sidebar-close')
        document.body.classList.add('f-page-editor-sidebar-open')
        saveState()
    },
    'hide-sidebar'() {
        document.body.classList.add('f-page-editor-sidebar-close')
        document.body.classList.remove('f-page-editor-sidebar-open')
        saveState()
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
    form.submit()
}

function initializeSortable(frameDocument) {
    const sectionElements = frameDocument.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag',
            onEnd() {
                saveState()
                updatePlugins()
            }
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

                saveState()

                submitForm(createForm, {
                    'CreateModel.DefinitionId': definitionId,
                    'CreateModel.Order': event.newIndex - 1,
                    'CreateModel.Section': sectionName
                })
            }
        });
    });
}

async function onInit() {
    const frameDocument = await getFrameDocument()

    const state = loadState()

    // state.sidebarOpen
    // if(true) {
    //     document.body.classList.add('f-page-editor-sidebar-open')
    // } else {
    //     document.body.classList.add('f-page-editor-sidebar-close')
    // }

    createForm = document.querySelector("#f-page-editor-form-create")
    updateForm = document.querySelector("#f-page-editor-form-update")
    deleteForm = document.querySelector("#f-page-editor-form-delete")

    if(state.scroll) {
        frameDocument.scrollingElement.scrollTo({
            top: state.scroll
        })
    }

    initializeActions(frameDocument)
    initializeActions(document)
    initializeSortable(frameDocument)
}

function saveState(done = false) {
    localStorage.setItem(localStorageKey, JSON.stringify({
        sidebarOpen: document.body.classList.contains('f-page-editor-sidebar-open') ? true : false,
        scroll: frameDocument.scrollingElement.scrollTop,
        done
    }))

    // if(done) {
    //     saveState()
    //     setTimeout(() => {
    //         window.location.href = window.location.href.replace('pageEdit', 'aaa')
    //     })
    // }

}

function loadState() {
    return JSON.parse(localStorage.getItem(localStorageKey) ?? '{}') ?? {}
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

document.addEventListener('fluentcms:init', () => {
    onInit()
})
