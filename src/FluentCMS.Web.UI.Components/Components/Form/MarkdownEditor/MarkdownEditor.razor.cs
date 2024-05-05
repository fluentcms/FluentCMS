namespace FluentCMS.Web.UI.Components;

public partial class MarkdownEditor
{
    public ElementReference element;

    private IJSObjectReference module = default!;

    [Parameter]
    public bool Readonly {get; set;} = false;

    [Parameter]
    public string? Placeholder {get; set;}

    private string? content;

    [Parameter]
    public string Value
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

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

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
