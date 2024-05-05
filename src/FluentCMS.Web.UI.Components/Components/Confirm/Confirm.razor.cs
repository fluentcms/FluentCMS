namespace FluentCMS.Web.UI.Components;

public partial class Confirm : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference element;

    private IJSObjectReference module = default!;

    [Parameter]
    public string? Id { get; set; }

    [Parameter]
    public string? Message { get; set; }

    private bool open;

    [Parameter]
    [CSSProperty]
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

            OpenChanged.InvokeAsync(Open);

            module.InvokeVoidAsync(open ? "open" : "close", DotNetObjectReference.Create(this), element);
        }
    }

    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    [Parameter]
    [CSSProperty]
    public ModalSize Size { get; set; } = ModalSize.Medium;

    [Parameter]
    [CSSProperty]
    public bool Static { get; set; }

    [Parameter]
    public EventCallback<MouseEventArgs> OnCancel { get; set; }

    [Parameter]
    public EventCallback<MouseEventArgs> OnConfirm { get; set; }

    [JSInvokable]
    public async void Close()
    {
        if (!Open) return;

        Open = false;

        await OnCancel.InvokeAsync();
    }

    public async void Confirmation()
    {
        if (!Open) return;

        Open = false;

        await OnConfirm.InvokeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Confirm/Confirm.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { Open, Static });
    }
}