namespace FluentCMS.Web.UI.Components;

public partial class MarkdownEditor : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference element;

    private IJSObjectReference module = default!;

    private string? content;

    [Parameter]
    public override string Value
    {
        get
        {
            return content;
        }
        set
        {
            if (value == content) return;

            content = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });
        }
    }

    [JSInvokable]
    public async Task UpdateValue(string value)
    {
        content = value;
        await ValueChanged.InvokeAsync(value);
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/MarkdownEditor/MarkdownEditor.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { });
    }
}
