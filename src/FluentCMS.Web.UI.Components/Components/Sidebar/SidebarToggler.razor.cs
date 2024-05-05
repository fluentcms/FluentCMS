namespace FluentCMS.Web.UI.Components;

public partial class SidebarToggler
{
    [Parameter]
    public string SidebarId { get; set; }

    private IJSObjectReference module = default!;

    bool IsOpen { get; set; } = false;

    [JSInvokable]
    public void Update(bool isOpen)
    {
        IsOpen = isOpen;
        StateHasChanged();
    }

    async Task OnClick()
    {
        await module.InvokeVoidAsync("toggle", DotNetObjectReference.Create(this), SidebarId);
        IsOpen = !IsOpen;
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), SidebarId);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import",
        "/_content/FluentCMS.Web.UI.Components/Components/Sidebar/SidebarToggler.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), SidebarId);

        IsOpen = false;
    }
}
