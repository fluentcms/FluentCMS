using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilder : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    public ElementReference element;

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<SiteBuilder> DotNetRef { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilder.razor.js");

        DotNetRef = DotNetObjectReference.Create(this);
        await Module.InvokeVoidAsync("initialize", DotNetRef, new { });
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