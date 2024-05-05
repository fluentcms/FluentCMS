namespace FluentCMS.Web.UI.Components;

public partial class Dropdown
{
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
    public async void Update(bool open)
    {
        await OpenChanged.InvokeAsync(Open = open);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Dropdown/Dropdown.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element);
    }
}
