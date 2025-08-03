namespace FluentCMS.Web.UI.Components;

public partial class Tooltip : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference? Element { get; set; }

    private IJSObjectReference? Module { get; set; }

    private DotNetObjectReference<Tooltip>? DotNetRef { get; set; }

    [Parameter]
    public TooltipPlacement? Placement { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Module is not null)
            {
                await Module.InvokeVoidAsync("dispose", DotNetRef, Element);
                await Module.DisposeAsync();
            }
            DotNetRef?.Dispose();
        }
        catch (Exception)
        {
            // 
        }

    }

    protected override async Task OnInitializedAsync()
    {
        DotNetRef = DotNetObjectReference.Create(this);
        await Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Tooltip/Tooltip.razor.js");

        // TODO: handle run time changing properties
        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, new { Placement = Placement?.ToString().FromPascalCaseToKebabCase() });
    }
}
