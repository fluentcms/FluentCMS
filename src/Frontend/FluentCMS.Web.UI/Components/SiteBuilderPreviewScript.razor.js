import { onInitPreview } from "/_content/FluentCMS.Web.UI/js/sitebuilder.preview.js";

export async function initialize(dotnet, config, ...args) {
    await onInitPreview(dotnet)
}