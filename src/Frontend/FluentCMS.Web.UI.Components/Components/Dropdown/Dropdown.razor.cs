namespace FluentCMS.Web.UI.Components;

public partial class Dropdown : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Parameter]
    public bool AutoClose { get; set; } = true;

    private bool open;

    [Parameter]
    public bool Open
    {
        get
        {
            return open;
        }
        set
        {
            if (value == open) return;

            open = value;

            Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), Element, new { open });
        }
    }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

	[Parameter]
	public RenderFragment ChildContent { get; set; } = default!;

	public ElementReference Element;

    public IJSObjectReference Module = default!;

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
        await OpenChanged.InvokeAsync(Open = open);
    }

    public async ValueTask DisposeAsync()
    {
        await Module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), Element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Dropdown/Dropdown.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element);
    }
}
