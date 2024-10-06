namespace FluentCMS.Web.UI.Components;

public partial class InlineEditor : IAsyncDisposable
{
    [Inject]
    private IJSRuntime? JS { get; set; }

    [Parameter]
    public string InitialValue { get; set; } = default!;

    [Parameter]
    public EventCallback<string> OnSave { get; set; }

    [Parameter]
    public EventCallback<string> OnCancel { get; set; }

    private ElementReference Element { get; set; }
    private DotNetObjectReference<InlineEditor> DotNetRef { get; set; } = default!;
    private IJSObjectReference Module { get; set; } = default!;

    private MarkupString Value { get; set; } = new();

    private bool IsEditing { get; set; } = false;

    [JSInvokable]
    public void UpdateContent(string content)
    {
        UpdateContent2(content);

        // StateHasChanged();
    }

    public void UpdateContent2(string content)        
    {
        IsEditing = true; 
        Value = (MarkupString)content;
    }
    private void ToggleIsEditing()
    {
        IsEditing = true;
    }

    protected override async Task OnInitializedAsync()
    {
        Value = (MarkupString)InitialValue;
        await Task.CompletedTask;
    }
 
    private async Task HandleSave()
    {
        await OnSave.InvokeAsync(Value.ToString());
        IsEditing = false;
        StateHasChanged();
        await Module.InvokeVoidAsync("reinitialize", DotNetRef, Element);
    }

    private async Task HandleCancel()
    {
        Value = (MarkupString)InitialValue;
        IsEditing = false;
        await OnCancel.InvokeAsync();
        StateHasChanged();
        await Module.InvokeVoidAsync("reinitialize", DotNetRef, Element);
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
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/InlineEditor/InlineEditor.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element);
    }
}
