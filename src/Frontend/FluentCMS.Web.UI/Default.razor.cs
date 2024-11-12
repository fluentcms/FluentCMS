using FluentCMS.Web.ApiClients.Services;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI;

public partial class Default
{
    [Inject]
    private AuthManager AuthManager { get; set; } = default!;

    [Inject]
    private ILayoutProcessor LayoutProcessor { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    public string? Route { get; set; }

    [Inject]
    public NavigationManager NavigationManager { set; get; } = default!;

    [Inject]
    public SetupManager SetupManager { set; get; } = default!;

    protected override async Task OnInitializedAsync()
    {

        if (!await SetupManager.IsInitialized())
        {
            if(HttpContext != null)
                await AuthManager.Logout(HttpContext);

            NavigationManager.NavigateTo("/setup", true);
        } 
    }

    protected RenderFragment RenderDynamicContent(string content) => builder =>
    {
        if (string.IsNullOrEmpty(content))
            return;

        var parameters = new Dictionary<string, object>
        {
            { "user", ViewState.User },
            { "site", ViewState.Site },
            { "page", ViewState.Page },
            { "plugins", ViewState.Plugins }
        };

        var segments = LayoutProcessor.ProcessSegments(content, parameters);
        var index = 0;
        foreach (var segment in segments)
        {
            if (segment.GetType() == typeof(HtmlSegment))
            {
                var htmlSegment = segment as HtmlSegment;
                builder.AddContent(index, (MarkupString)htmlSegment!.Content);
            }
            else if (segment.GetType() == typeof(ComponentSegment))
            {
                var componentSegment = segment as ComponentSegment;
                builder.OpenComponent(index, componentSegment!.Type);

                var attributeIndex = 0;
                foreach (var attribute in componentSegment.Attributes)
                {
                    builder.AddComponentParameter(attributeIndex, attribute.Key, attribute.Value);
                    attributeIndex++;
                }
                builder.AddComponentRenderMode(PluginRenderMode());

                builder.CloseComponent();
            }
            index++;
        }
    };

    private InteractiveServerRenderMode? PluginRenderMode()
    {
        if (ViewState.Page.Locked)
            return null;

        if (ViewState.Type == ViewStateType.PagePreview || ViewState.Type == ViewStateType.PageEdit)
            return RenderMode.InteractiveServer;

        if (ViewState.Type == ViewStateType.Default && ViewState.Page.HasAdminAccess)
            return RenderMode.InteractiveServer;

        return null;
    }
}
