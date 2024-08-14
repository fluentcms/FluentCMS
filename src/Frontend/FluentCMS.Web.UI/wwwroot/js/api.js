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
            el.querySelectorAll('.f-plugin-container').forEach(plugin => {
                const pluginId = plugin.dataset.id
                result[`UpdatePluginOrdersModel.Plugins[${index}]`] = pluginId
                index++;
            })
        })
        
        await request('UpdatePluginOrdersForm', result)
    }, 200)
}