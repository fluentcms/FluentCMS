import { reloadIframe, request } from "./request.js";

export async function createPlugin({definitionId, sectionName, item}) {
    await request('CreatePluginForm', {
        'CreatePluginModel.DefinitionId': definitionId,
        'CreatePluginModel.PageId': '00000000-0000-0000-0000-000000000000',
        'CreatePluginModel.Order': 1,
        'CreatePluginModel.Cols': 12,
        'CreatePluginModel.ColsMd': 0,
        'CreatePluginModel.ColsLg': 0,
        'CreatePluginModel.Section': sectionName,
    }).then(reloadIframe)
}