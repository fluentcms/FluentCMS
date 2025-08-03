namespace FluentCMS.Web.UI.Components;

public partial class Accordion : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Disabled { get; set; }

    [Parameter]
    public string Summary { get; set; } = string.Empty;


    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    private bool IsOpen { get; set; }

    private DotNetObjectReference<Accordion>? DotNetRef { get; set; }
    public ElementReference? Element { get; set; }
    public IJSObjectReference? Module { get; set; }

    [JSInvokable]
    public async Task Update(bool open)
    {
        if (open == Open) return;

        Open = open;

        if (Module is null)
            return;

        await Module.InvokeVoidAsync(Open ? "open" : "close", DotNetRef, Element);
        await OpenChanged.InvokeAsync(Open);
    }

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
        catch(Exception ex)
        {
            //
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Accordion/Accordion.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element);
    }
}
