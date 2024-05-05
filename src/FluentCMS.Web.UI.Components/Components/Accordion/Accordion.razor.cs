using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public partial class Accordion : BaseComponent 
{
    [Inject]
    private IJSRuntime JS { get; set; }
    
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

    public ElementReference Element;

    public IJSObjectReference Module = default!;

    [JSInvokable]
    public async void Update(bool open)
    {
        await OpenChanged.InvokeAsync(Open = open);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Accordion/Accordion.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element);
    }
}
