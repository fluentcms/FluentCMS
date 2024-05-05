namespace FluentCMS.Web.UI.Components;

public partial class Tooltip
{
    public ElementReference element;

    private IJSObjectReference module = default!;

    [Parameter]
    public TooltipPlacement? Placement { get; set; }

    public void Dispose()
    {
        module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Tooltip/Tooltip.razor.js");

        // TODO: handle run time changing properties
        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { Placement = Placement?.ToString().FromPascalCaseToKebabCase() });
    }
}