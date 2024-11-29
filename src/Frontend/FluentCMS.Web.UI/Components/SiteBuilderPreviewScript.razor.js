import '/_content/FluentCMS.Web.UI/js/sortable.js'
import {Columns} from '/_content/FluentCMS.Web.UI/js/columns.js'
import { initializeClickEvents } from '/_content/FluentCMS.Web.UI/js/actions.js'

let sections = {}

export function initColumns() {
    for(let section in sections ?? {}) {
        sections[section].destroy()
    }
    sections = {}
    
    document.querySelectorAll('.f-section').forEach(section => {
        const column = new Columns(section, {
            doc: document,
            gridLines: true,
            colClass: 'f-plugin-container',
            breakpointLg: 992,
            breakpointMd: 480,
            onResize(el) {
                updatePluginCols(el)
            }
        })
        
        setTimeout(() => {
            column.init()
        }, 100)
        sections[section.dataset.name] = column
    })
}

export async function updatePluginCols(pluginEl) {
    const pluginId = pluginEl.dataset.id
    const cols = +pluginEl.dataset.cols
    const colsMd = +pluginEl.dataset.colsMd
    const colsLg = +pluginEl.dataset.colsLg

    const plugin = {}
    plugin.Id = pluginId
    plugin.Cols = cols
    plugin.ColsMd = colsMd
    plugin.ColsLg = colsLg
    
    await dotnet.invokeMethodAsync("UpdatePluginCols", plugin)
}

export async function updatePluginOrders() {
    const sections = document.querySelectorAll('.f-section')

    let result = []

    sections.forEach(el => {
        el.querySelectorAll('.f-plugin-container').forEach(plugin => {
            
            var plugin = {
                Id: plugin.dataset.id,
                Section: el.dataset.name,
            }
            
            result.push(plugin)
        })
    })
    
    await dotnet.invokeMethodAsync("UpdatePluginsOrder", result)
}

export function initializeSortable() {
    const sectionElements = document.querySelectorAll('.f-section');

    sectionElements.forEach(section => {
        new Sortable(section, {
            animation: 150,
            direction: 'vertical',
            group: 'shared',
            draggable: '.f-plugin-container',
            ghostClass: 'f-plugin-container-moving',
            chosenClass: 'f-plugin-container-chosen',
            handle: '.f-plugin-container-action-drag',
            onEnd() {
                updatePluginOrders()
            }
        });
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
        async onEnd(event) {
            if(event.from === event.to) return;
            const definitionId = event.clone.dataset.id
            const item = event.item
            const sectionName = event.to.dataset.name

            const newPluginId = await window.dotnet.invokeMethodAsync("CreatePlugin", definitionId, sectionName)
            
            item.dataset.id = newPluginId
            item.classList.add('f-plugin-container')

            updatePluginOrders()
            document.querySelectorAll('.f-section > .f-plugin-definition-item').forEach(item => item.remove())
        }
    });
}

export async function initialize(dotnet) {
    window.dotnet = dotnet
    initializeSortable(document)
    initColumns(document)   
    initializeClickEvents(document)
}

export async function update(dotnet) {
    window.dotnet = dotnet
    initializeSortable(document)
    initColumns(document)   
    initializeClickEvents(document)
}