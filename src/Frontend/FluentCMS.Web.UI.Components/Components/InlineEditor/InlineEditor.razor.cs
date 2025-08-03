namespace FluentCMS.Web.UI.Components;

public partial class InlineEditor : IAsyncDisposable
{
    [Inject]
    private IJSRuntime? JS { get; set; }

    [Parameter]
    public string Value { get; set; } = default!;

    [Parameter]
    public EventCallback<string> OnSave { get; set; }

    [Parameter]
    public EventCallback<string> OnCancel { get; set; }

    private ElementReference Element { get; set; }
    private DotNetObjectReference<InlineEditor> DotNetRef { get; set; } = default!;
    private IJSObjectReference Module { get; set; } = default!;

    [JSInvokable]
    public async Task Save(string content)
    {
        Value = content;
        await OnSave.InvokeAsync(Value.ToString());
        await Module.InvokeVoidAsync("reinitialize", DotNetRef, Element, Value);
    }

    [JSInvokable]
    public async Task Cancel()
    {
        await OnCancel.InvokeAsync();
        await Module.InvokeVoidAsync("reinitialize", DotNetRef, Element, Value);
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
        if (!firstRender)
            return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/InlineEditor/InlineEditor.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, Value);
    }
}
