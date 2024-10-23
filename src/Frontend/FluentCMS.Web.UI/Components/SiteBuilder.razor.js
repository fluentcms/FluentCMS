import { initializeResponsive } from '/_content/FluentCMS.Web.UI/js/responsive.js'
import { initializeClickEvents } from '/_content/FluentCMS.Web.UI/js/actions.js'

function calculateScrollbarWidth() {
    var scrollDiv = document.createElement("div");
    
	scrollDiv.style.width = '100px';
	scrollDiv.style.height = '100px';
	scrollDiv.style.overflow = 'scroll';
	scrollDiv.style.position = 'absolute';
	scrollDiv.style.top = '-9999px';
    document.body.appendChild(scrollDiv);

    var scrollbarWidth = scrollDiv.offsetWidth - scrollDiv.clientWidth;
    document.body.removeChild(scrollDiv);
    
    document.querySelector('.f-toolbar-end').style.marginRight = scrollbarWidth + 'px'
}

export async function initialize(dotnet) {
    window.dotnet = dotnet
    initializeResponsive()
    initializeClickEvents(document)

    calculateScrollbarWidth()
}