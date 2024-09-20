import { closePluginsSidebar } from "./request.js";

export async function createPlugin({definitionId, sectionName, order}) {
    closePluginsSidebar()
    await window.dotnet.invokeMethodAsync("CreatePlugin", definitionId, sectionName, order)
    await updatePluginOrders()
}

export async function updatePluginCols(pluginEl, section) {
    const pluginId = pluginEl.dataset.id
    const cols = +pluginEl.dataset.cols
    const colsMd = +pluginEl.dataset.colsMd
    const order = +pluginEl.dataset.order
    const colsLg = +pluginEl.dataset.colsLg

    const plugin = {}
    plugin.Id = pluginId
    plugin.Section = section.dataset.name
    plugin.Order = order
    plugin.Cols = cols
    plugin.ColsMd = colsMd
    plugin.ColsLg = colsLg
    
    await dotnet.invokeMethodAsync("UpdatePluginsOrder", [plugin])
}

export async function updatePlugin(pluginContainerEl, sectionEl) {
    let result = {}

    const plugin = pluginContainerEl
    const pluginId = plugin.dataset.id
    const section = sectionEl.dataset.name
    const order = +plugin.dataset.order

    const cols = plugin.dataset.cols
    const colsMd = plugin.dataset.colsMd
    const colsLg = plugin.dataset.colsLg

    result.Id = pluginId
    result.Section = section
    result.Order = order
    result.Cols = cols
    result.ColsMd = colsMd
    result.ColsLg = colsLg
           
    await dotnet.invokeMethodAsync('UpdatePlugin', result);
}


export async function updatePluginOrders() {
    setTimeout(async () => {
        const sections = document.querySelectorAll('.f-section')

        let result = []

        sections.forEach(el => {
            let order = 0;
            el.querySelectorAll('.f-plugin-container').forEach(plugin => {
                const pluginId = plugin.dataset.id
                const cols = +plugin.dataset.cols
                const colsMd = +plugin.dataset.colsMd
                const colsLg = +plugin.dataset.colsLg

                var plugin = {
                    Id: pluginId,
                    Section: el.dataset.name,
                    Cols: cols,
                    Order: ++order,
                    ColsMd: colsMd,
                    ColsLg: colsLg
                }
              
                result.push(plugin)
            })
        })
        
        await dotnet.invokeMethodAsync("UpdatePluginsOrder", result)
    })
}