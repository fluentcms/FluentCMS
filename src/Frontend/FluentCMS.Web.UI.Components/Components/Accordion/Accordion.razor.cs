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

            Module.InvokeVoidAsync(open ? "open" : "close", DotNetObjectReference.Create(this), Element);
        }
    }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;

    public ElementReference Element;

    public IJSObjectReference Module = default!;

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

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Accordion/Accordion.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element);
    }
}
