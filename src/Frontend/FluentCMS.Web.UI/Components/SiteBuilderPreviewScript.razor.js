import { onInitPreview, onUpdatePreview } from "/_content/FluentCMS.Web.UI/js/sitebuilder.preview.js";

export async function initialize(dotnet) {
    console.log('init preview')
    await onInitPreview(dotnet)
}

export async function update(dotnet) {
    console.log('update preview')
    await onUpdatePreview(dotnet)
}