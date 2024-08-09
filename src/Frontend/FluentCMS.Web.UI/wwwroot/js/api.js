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

    await updatePlugins()
}

export async function deletePlugin(id) {
    await request('DeletePluginForm', {
        'DeletePluginModel': id,
    }).then(reloadIframe)
}

export async function updatePlugins() {
    const sections = document.querySelector('.f-page-editor-iframe').contentDocument.querySelectorAll('.f-section')

    console.log({sections})
    let result = {}

    result[`UpdatePluginsModel.Submitted`] = true

    let index = 0;
    sections.forEach(el => {
        let order = 0;
        el.querySelectorAll('.f-plugin-container').forEach(plugin => {
            const pluginId = plugin.dataset.id
            const section = el.dataset.name

            const cols = plugin.dataset.cols
            const colsMd = plugin.dataset.colsMd
            const colsLg = plugin.dataset.colsLg

            
            result[`UpdatePluginsModel.Plugins[${index}].Id`] = pluginId
            result[`UpdatePluginsModel.Plugins[${index}].Id`] = pluginId
            result[`UpdatePluginsModel.Plugins[${index}].Section`] = section
            result[`UpdatePluginsModel.Plugins[${index}].Order`] = order++
            result[`UpdatePluginsModel.Plugins[${index}].Cols`] = cols
            result[`UpdatePluginsModel.Plugins[${index}].ColsMd`] = colsMd
            result[`UpdatePluginsModel.Plugins[${index}].ColsLg`] = colsLg
                
            index++;
        })
    })
    
    await request('UpdatePluginsForm', result)
}