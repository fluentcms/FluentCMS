import { hydrate, initColumns, initializeSortable } from "./request.js";

export async function onInitPreview(dotnet) {
    console.log('init preview')
    window.dotnet = dotnet
    initializeSortable(document)
    initColumns(document)   
    hydrate(document)

    // toggle f-page-editor-sidebar-open
}