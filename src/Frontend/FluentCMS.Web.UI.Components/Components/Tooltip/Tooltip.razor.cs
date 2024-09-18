namespace FluentCMS.Web.UI.Components;

public partial class Tooltip : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference element;

    private IJSObjectReference Module { get; set; } = default!;

    [Parameter]
    public TooltipPlacement? Placement { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public async ValueTask DisposeAsync()
    {
        await Module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Tooltip/Tooltip.razor.js");

        // TODO: handle run time changing properties
        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { Placement = Placement?.ToString().FromPascalCaseToKebabCase() });
    }
}
