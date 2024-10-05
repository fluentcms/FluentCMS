using Microsoft.JSInterop;

namespace FluentCMS.Web.Plugins.Contents.Block;

public partial class BlockInlineEditable : IAsyncDisposable
{
    [Inject]
    private ApiClientFactory ApiClients { get; set; } = default!;

    [Inject]
    private IJSRuntime? JS { get; set; }

    [Parameter]
    public BlockContent Item { get; set; } = default!;

    [Parameter]
    public PluginViewState Plugin { get; set; } = default!;

    private ElementReference Element { get; set; }
    private DotNetObjectReference<BlockInlineEditable> DotNetRef { get; set; } = default!;
    private IJSObjectReference Module { get; set; } = default!;

    private string Content { get; set; } = string.Empty;
    private bool IsEditing { get; set; } = false;

    [JSInvokable]
    public void UpdateContent(string content)
    {
        Content = content;
        IsEditing = true;
        StateHasChanged();
    }

    protected override async Task OnInitializedAsync()
    {
        Content = Item.Content;
        await Task.CompletedTask;
    }

    private async Task OnSave()
    {
        Item.Content = Content;
        await ApiClients.PluginContent.UpdateAsync(nameof(BlockContent), Plugin.Id, Item.Id, Item.ToDictionary());
        IsEditing = false;
    }

    private async Task OnCancel()
    {
        Content = Item.Content;
        IsEditing = false;
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Module is not null)
        {
            await Module.InvokeVoidAsync("dispose", DotNetRef, Element);
            await Module.DisposeAsync();
        }
        DotNetRef?.Dispose();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.Plugins.Contents.Block/Components/BlockInlineEditable.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element);
    }
}
