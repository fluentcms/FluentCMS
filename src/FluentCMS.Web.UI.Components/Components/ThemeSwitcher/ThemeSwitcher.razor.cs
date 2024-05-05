namespace FluentCMS.Web.UI.Components;

public partial class ThemeSwitcher
{
    private IJSObjectReference module = default!;

    bool IsLight { get; set; }

    [JSInvokable]
    public async void Update(bool isLight)
    {
        IsLight = isLight;
        StateHasChanged();
    }

    async Task OnClick()
    {
        await module.InvokeVoidAsync("toggle", DotNetObjectReference.Create(this), !IsLight);
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/ThemeSwitcher/ThemeSwitcher.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this));
    }
}
