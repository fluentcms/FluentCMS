using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormMarkdownEditor : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference element;

    private IJSObjectReference Module { get; set; } = default!;

    [Parameter]
    public int Cols { get; set; } = 12;

    private string? _value;

    [JSInvokable]
    public async Task UpdateValue(string value)
    {
        _value = value;
        await ValueChanged.InvokeAsync(value);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Value == _value) return;

        _value = Value;
        await Module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });
    }

    public async ValueTask DisposeAsync()
    {
        if (Module != null)
            await Module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormMarkdownEditor/FormMarkdownEditor.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { });
    }

    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}
