import { reloadIframe, request } from "./request.js";

export async function createPlugin({definitionId, sectionName, order}) {
    await request('CreatePluginForm', {
        'CreatePluginModel.DefinitionId': definitionId,
        'CreatePluginModel.PageId': '00000000-0000-0000-0000-000000000000',
        'CreatePluginModel.Order': order,
        'CreatePluginModel.Cols': 12,
        'CreatePluginModel.ColsMd': 0,
        'CreatePluginModel.ColsLg': 0,
        'CreatePluginModel.Section': sectionName,
    }).then(reloadIframe)

    await updatePluginOrders()
}

export async function deletePlugin(id) {
    await request('DeletePluginForm', {
        'DeletePluginModel': id,
    }).then(reloadIframe)

    await updatePluginOrders()
}

export async function updatePluginCols(pluginEl, section) {
    const result = {}
    const pluginId = pluginEl.dataset.id
    const cols = pluginEl.dataset.cols
    const colsMd = pluginEl.dataset.colsMd
    const order = +pluginEl.dataset.order
    const colsLg = pluginEl.dataset.colsLg
    result[`UpdatePluginOrdersModel.Plugins[0].Id`] = pluginId
    result[`UpdatePluginOrdersModel.Plugins[0].Section`] = section.dataset.name
    result[`UpdatePluginOrdersModel.Plugins[0].Order`] = order
    result[`UpdatePluginOrdersModel.Plugins[0].Cols`] = cols
    result[`UpdatePluginOrdersModel.Plugins[0].ColsMd`] = colsMd
    result[`UpdatePluginOrdersModel.Plugins[0].ColsLg`] = colsLg
    
    await request('UpdatePluginOrdersForm', result)

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

    result[`UpdatePluginModel.Id`] = pluginId
    result[`UpdatePluginModel.Section`] = section
    result[`UpdatePluginModel.Order`] = order
    result[`UpdatePluginModel.Cols`] = cols
    result[`UpdatePluginModel.ColsMd`] = colsMd
    result[`UpdatePluginModel.ColsLg`] = colsLg
           
    await request('UpdatePluginForm', result)
}


export async function updatePluginOrders() {
    setTimeout(async () => {
        const sections = document.querySelector('.f-page-editor-iframe').contentDocument.querySelectorAll('.f-section')

        let result = {}

        let index = 0;
        sections.forEach(el => {
            let order = 0;
            el.querySelectorAll('.f-plugin-container').forEach(plugin => {
                const pluginId = plugin.dataset.id
                const cols = plugin.dataset.cols
                const colsMd = plugin.dataset.colsMd
                const colsLg = plugin.dataset.colsLg
                result[`UpdatePluginOrdersModel.Plugins[${index}].Id`] = pluginId
                result[`UpdatePluginOrdersModel.Plugins[${index}].Section`] = el.dataset.name
                result[`UpdatePluginOrdersModel.Plugins[${index}].Cols`] = cols
                result[`UpdatePluginOrdersModel.Plugins[${index}].Order`] = ++order
                result[`UpdatePluginOrdersModel.Plugins[${index}].ColsMd`] = colsMd
                result[`UpdatePluginOrdersModel.Plugins[${index}].ColsLg`] = colsLg
                index++;
            })
        })
        
        await request('UpdatePluginOrdersForm', result)
    }, 200)
}