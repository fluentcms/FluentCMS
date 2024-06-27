using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormRichTextEditor
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    public ElementReference element;

    private IJSObjectReference module = default!;

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

        if (module != null)
            await module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });
    }

    public async ValueTask DisposeAsync()
    {
        if (module != null)
            await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormRichTextEditor/FormRichTextEditor.razor.js");

        // TODO: type should be property
        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element, new { Value = Value, Readonly = Readonly, Placeholder = Placeholder, Type = "advanced" });
    }
    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}