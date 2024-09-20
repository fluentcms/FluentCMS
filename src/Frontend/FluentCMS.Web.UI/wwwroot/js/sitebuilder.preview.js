import { hydrate, initColumns, initializeSortable } from "./request.js";

export async function onInitPreview(dotnet) {
    window.dotnet = dotnet
    initializeSortable(document)
    initColumns(document)   
    hydrate(document)
}

export async function onUpdatePreview(dotnet) {
    // TODO: Only run once

    window.dotnet = dotnet
    initializeSortable(document)
    initColumns(document)   
    hydrate(document)
}