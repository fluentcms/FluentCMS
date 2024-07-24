import { closeOffcanvas, openOffcanvas } from "./helpers.js"

export function onCloseOffcanvas()
{
    closeOffcanvas()
}
export function onPagesList() {
    openOffcanvas('f-offcanvas-pages-list')
    // 
}

export function onPageSettings() {
    openOffcanvas('f-offcanvas-page-settings')    
}

