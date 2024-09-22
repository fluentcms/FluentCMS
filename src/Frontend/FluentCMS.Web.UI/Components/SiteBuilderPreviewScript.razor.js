import { onInitPreview, onUpdatePreview } from "/_content/FluentCMS.Web.UI/js/sitebuilder.preview.js";

export async function initialize(dotnet) {
    await onInitPreview(dotnet)
}

export async function update(dotnet) {
    await onUpdatePreview(dotnet)
}