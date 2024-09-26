import { initializeResponsive } from '/_content/FluentCMS.Web.UI/js/responsive.js'
import { initializeClickEvents } from '/_content/FluentCMS.Web.UI/js/actions.js'

export async function initialize(dotnet) {
    window.dotnet = dotnet
    initializeResponsive()
    initializeClickEvents(document)
}