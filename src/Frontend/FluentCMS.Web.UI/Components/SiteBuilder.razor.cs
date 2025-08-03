using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilder : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    private IJSObjectReference? Module { get; set; }

    private DotNetObjectReference<SiteBuilder>? DotNetRef { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilder.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Module is not null)
            {
                await Module.DisposeAsync();
            }

            if (DotNetRef != null)
                DotNetRef.Dispose();
        } 
        catch(Exception ex)
        {
            // 
        }  
    }

}