using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderFloatingButtons : IAsyncDisposable
{
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private IJSRuntime? JS { get; set; }

    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private ElementReference TogglerRef { get; set; } = default!;
    private ElementReference ButtonsRef { get; set; } = default!;
    private DotNetObjectReference<SiteBuilderFloatingButtons> DotNetRef { get; set; } = default!;
    private IJSObjectReference Module { get; set; } = default!;

    private string GetPageAddUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var redirectTo = Uri.EscapeDataString(uri.PathAndQuery);
        var queryParams = new Dictionary<string, string?>()
        {
            { "viewType", "Create" },
            { "redirectTo", redirectTo }
        };

        var queryParamsString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}"));

        return $"/admin/pages?{queryParamsString}";
    }

    private string GetPageEditUrl()
    {
        var uri = new Uri(NavigationManager.Uri);
        var redirectTo = Uri.EscapeDataString(uri.PathAndQuery);

        var queryParams = new Dictionary<string, string?>()
        {
            { "Id", ViewState.Page.Id.ToString() },
            { "redirectTo", redirectTo }
        };

        var queryParamsString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value ?? string.Empty)}"));

        return $"/admin/pages?{queryParamsString}";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderFloatingButtons.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, TogglerRef, ButtonsRef);
    }

    public async ValueTask DisposeAsync()
    {
        if (Module is not null)
        {
            await Module.DisposeAsync();
        }
        DotNetRef.Dispose();
    }
}
