namespace FluentCMS.Web.UI.Components;

public partial class Dropdown : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Parameter]
    public bool AutoClose { get; set; } = true;


    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    private bool IsOpen { get; set; }

    private DotNetObjectReference<Dropdown>? DotNetRef { get; set; }
    private ElementReference? Element { get; set; }
    private IJSObjectReference? Module { get; set; }

    public void Close()
    {
        if (AutoClose)
        {
            Open = false;
        }
    }

    [JSInvokable]
    public async Task Update(bool open)
    {
        if (open == Open) return;

        Open = open;

        if (Module is null)
            return;

        await Module.InvokeVoidAsync("update", DotNetRef, Element, new { Open });
        if (OpenChanged.HasDelegate)
        {
            await OpenChanged.InvokeAsync(Open);
        }
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
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Dropdown/Dropdown.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, new { Open = IsOpen });
    }
}
