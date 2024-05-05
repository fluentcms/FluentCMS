namespace FluentCMS.Web.UI.Components;

public partial class Browse
{    
    [Parameter]
    public string? Accept { get; set; }

    [Parameter]
    public bool Multiple { get; set; }

    [Parameter]
    public EventCallback<InputFileChangeEventArgs> OnChange { get; set; }

    public ElementReference Element;

    public IJSObjectReference Module = default!;

    async Task HandleChange(InputFileChangeEventArgs evt)
    {
        OnChange.InvokeAsync(evt);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Browse/Browse.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), Element);
    }
}
