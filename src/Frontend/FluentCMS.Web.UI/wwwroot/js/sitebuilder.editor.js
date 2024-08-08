import {request, hydrate, initializeSortable, initColumns} from './request.js'
import { initializeResponsive, updateResizerPosition } from './responsive.js'
import './sortable.js'

let iframeElement = document.querySelector('.f-page-editor-iframe')
let pageEditorElement = document.querySelector('.f-page-editor')


// request('TEST', {abc: '123'})

let frameDocument;
function getFrameDocument() {
    return new Promise(resolve => {
        iframeElement.onload = () => {
            frameDocument = iframeElement.contentDocument;
            resolve(frameDocument)
        }
    })
}

let updateForm = document.querySelector("#f-page-editor-form-update")

function save() {
    const sections = {}

    frameDocument.querySelectorAll('.f-section').forEach(section => {
        sections[section.dataset.name] = []

        section.querySelectorAll('.f-plugin-container').forEach(plugin => {
            sections[section.dataset.name].push(plugin.dataset)
        })
    })

    var updateInputs = '<input type="hidden" name="Model.Submitted" value="true" />'
    let deleteIndex = 0;
    let newPluginsIndex = 0;
    let updatedPluginsIndex = 0;
    let pluginOrder = 0;
    for(let section in sections) {
        for(let index in sections[section]) {
            const plugin = sections[section][index].id
            const cols = sections[section][index].cols
            const colsMd = sections[section][index].colsMd
            const colsLg = sections[section][index].colsLg
            const definitionId = sections[section][index].definitionId
            const deleted = sections[section][index].deleted

            if(deleted) {
                updateInputs += `
                    <input type="hidden" name="Model.DeleteIds[${deleteIndex}]" value="${plugin}" />
                `
                deleteIndex++;
            }
            else if(plugin == '00000000-0000-0000-0000-000000000000') {
                // PageId will be filled in backend
                updateInputs += `
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].PageId" value="00000000-0000-0000-0000-000000000000" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].DefinitionId" value="${definitionId}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Order" value="${pluginOrder}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Cols" value="${cols}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsMd" value="${colsMd}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].ColsLg" value="${colsLg}" />
                    <input type="hidden" name="Model.CreatePlugins[${newPluginsIndex}].Section" value="${section}" />
                `
                pluginOrder++;
                newPluginsIndex++;
            }
            else {
                updateInputs += `
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Section" value="${section}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Id" value="${plugin}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Order" value="${pluginOrder}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].Cols" value="${cols}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsMd" value="${colsMd}" />
                    <input type="hidden" name="Model.UpdatePlugins[${updatedPluginsIndex}].ColsLg" value="${colsLg}" />
                `
                pluginOrder++;
                updatedPluginsIndex++;
            }
        }
    }

    updateForm.querySelector('#f-update-form-inputs').outerHTML = updateInputs
    setTimeout(() => updateForm.submit())
}


async function onInit() {
    const frameDocument = await getFrameDocument()

    initializeResponsive()

    hydrate(frameDocument)
    hydrate(document)
    initializeSortable(frameDocument)
    initColumns(frameDocument)
}

document.addEventListener('fluentcms:afterenhanced', () => {
    onInit()
})

onInit()