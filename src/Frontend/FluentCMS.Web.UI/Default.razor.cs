using AutoMapper;
using FluentCMS.Web.ApiClients.Services;
using FluentCMS.Web.UI.DynamicRendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
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

    private bool Authenticated { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        NavigationManager.LocationChanged += LocationChanged;
        await Task.CompletedTask;
    }

    protected override async Task OnParametersSetAsync()
    {
        Authenticated = ViewState.Type == ViewStateType.Default && !ViewState.Page.Locked && ViewState.User.Roles.Any(role => role.Type == RoleTypesViewState.Authenticated);

        // check if setup is not done
        // if not it should be redirected to /setup route
        if (!await SetupManager.IsInitialized() && !NavigationManager.Uri.ToLower().EndsWith("/setup"))
        {
            if (HttpContext != null)
                await AuthManager.Logout(HttpContext);

            NavigationManager.NavigateTo("/setup", true);
            return;
        }
    }

    void LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        ViewState.Reload();
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        NavigationManager.LocationChanged -= LocationChanged;
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

        foreach (var segment in segments)
        {
            if (segment.GetType() == typeof(HtmlSegment))
            {
                var htmlSegment = segment as HtmlSegment;
                builder.AddContent(0, (MarkupString)htmlSegment!.Content);
            }
            else if (segment.GetType() == typeof(ComponentSegment))
            {
                var componentSegment = segment as ComponentSegment;
                builder.OpenComponent(1, componentSegment!.Type);

                foreach (var attribute in componentSegment.Attributes)
                {
                    builder.AddComponentParameter(2, attribute.Key, attribute.Value);
                }
                builder.AddComponentRenderMode(PluginRenderMode());

                builder.CloseComponent();
            }
        }
    };

    private InteractiveServerRenderMode? PluginRenderMode()
    {
        if (ViewState.Page.Locked)
            return null;

        if (ViewState.Type == ViewStateType.PagePreview || ViewState.Type == ViewStateType.PageEdit)
            return RenderMode.InteractiveServer;

        if (ViewState.Type == ViewStateType.Default && ViewState.User.Roles.Any(x => x.Type == RoleTypesViewState.Authenticated))
            return RenderMode.InteractiveServer;

        return null;
    }
}
