namespace FluentCMS.Web.UI.Components;

public partial class Tooltip : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference Element { get; set; }

    private IJSObjectReference Module { get; set; } = default!;

    private DotNetObjectReference<Tooltip> DotNetRef { get; set; } = default!;

    [Parameter]
    public TooltipPlacement? Placement { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public async ValueTask DisposeAsync()
    {
        if (Module is not null)
        {
            await Module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), Element);
            await Module.DisposeAsync();
        }
        DotNetRef.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Tooltip/Tooltip.razor.js");

        // TODO: handle run time changing properties
        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, new { Placement = Placement?.ToString().FromPascalCaseToKebabCase() });
    }
}
