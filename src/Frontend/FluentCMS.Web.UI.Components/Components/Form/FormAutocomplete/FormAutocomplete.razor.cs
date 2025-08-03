using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluentCMS.Web.UI.Components;

public partial class FormAutocomplete<TItem, TValue> : IAsyncDisposable
{
    [Inject]
    public IJSRuntime? JS { get; set; }

    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public ICollection<TItem> Data { get; set; } = [];

    [Parameter]
    public string? TextField { get; set; }

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    public ElementReference? Element { get; set; }
    private IJSObjectReference? Module { get; set; }
    private DotNetObjectReference<FormAutocomplete<TItem, TValue>>? DotNetRef { get; set; }

    private bool IsSelected(TItem item)
    {
        return Value?.ToString() == GetValue(item)?.ToString();
    }

    private string? GetText(TItem item)
    {
        if (string.IsNullOrEmpty(TextField))
        {
            return item!.ToString();
        }
        return (string?)item!.GetType().GetProperty(TextField)?.GetValue(item);
    }

    private TValue? GetValue(TItem item)
    {
        if (string.IsNullOrEmpty(ValueField))
        {
            return (TValue?)(object)item!;
        }
        return (TValue?)item!.GetType().GetProperty(ValueField)?.GetValue(item);
    }

    [JSInvokable]
    public async Task UpdateValue(string value)
    {
        if (BindConverter.TryConvertTo<TValue>(value, CultureInfo.CurrentCulture, out var parsedValue))
        {
            Value = parsedValue;
        }
        await ValueChanged.InvokeAsync(Value);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Module != null)
            await Module.InvokeVoidAsync("update", DotNetRef, Element, new { Value });
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
        if (!firstRender) return;

        if (JS is null)
        {
            throw new InvalidOperationException("JS runtime has not been initialized.");
        }

        DotNetRef = DotNetObjectReference.Create(this);
        Module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/FormAutocomplete/FormAutocomplete.razor.js");

        await Module.InvokeVoidAsync("initialize", DotNetRef, Element, new { Multiple = false });
    }

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        try
        {
            if (BindConverter.TryConvertTo<TValue>(value, CultureInfo.CurrentCulture, out var parsedValue))
            {
                result = parsedValue;
                validationErrorMessage = null;
                return true;
            }

            result = default;
            validationErrorMessage = $"The {DisplayName ?? FieldIdentifier.FieldName} field is not valid.";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{GetType()} does not support the type '{typeof(TValue)}'.", ex);
        }
    }
}

