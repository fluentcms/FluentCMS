import { hydrate, initializeSortable, initColumns } from './request.js'
import { initializeResponsive } from './responsive.js'


export async function onInitEditor(dotnet) {
    initializeResponsive()
    hydrate(document)
}