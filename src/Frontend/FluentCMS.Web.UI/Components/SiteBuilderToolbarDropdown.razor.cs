using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilderToolbarDropdown : IAsyncDisposable
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<SiteBuilderToolbarDropdown>? DotNetRef { get; set; } = default!;

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        try {
            if (Module is not null)
            {
                await Module.DisposeAsync();
            }

            DotNetRef?.Dispose();
        } 
        catch(Exception ex)
        {
            // 
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilderToolbarDropdown.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef);
    }
}
