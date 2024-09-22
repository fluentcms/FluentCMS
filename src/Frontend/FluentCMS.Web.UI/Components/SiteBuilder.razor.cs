using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class SiteBuilder
{
    [Inject]
    public IJSRuntime JS { get; set; } = default!;

    public ElementReference element;

    private IJSObjectReference module = default!;

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

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI/Components/SiteBuilder.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), new {});
    }
}