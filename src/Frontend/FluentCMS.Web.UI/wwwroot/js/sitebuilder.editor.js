import { hydrate, initializeSortable, initColumns } from './request.js'
import { initializeResponsive } from './responsive.js'


export async function onInitEditor(dotnet) {
    console.log('init editor', dotnet)

    initializeResponsive()

    hydrate(document)
}