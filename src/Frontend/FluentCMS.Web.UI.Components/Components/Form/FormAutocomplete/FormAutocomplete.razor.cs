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
    public RenderFragment ChildContent { get; set; }


    [Parameter]
    public bool Disabled
    {
        get => disabled;
        set
        {
            this.disabled = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Disabled });
        }
    }

    [Parameter]
    public bool Multiple { get; set; }

    private List<AutocompleteOptions> options = [];

    public ElementReference element;

    private IJSObjectReference module = default!;

    private bool disabled;

    [Parameter]
    public List<AutocompleteOptions> Options
    {
        get => options;
        set
        {
            if (this.options.SequenceEqual(value)) return;

            this.options = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Options });
        }
    }

    private List<string> value = [];

    [Parameter]
    public List<string> Value
    {
        get => this.value;
        set
        {
            if (this.value.SequenceEqual(value)) return;

            this.value = value;

            module.InvokeVoidAsync("update", DotNetObjectReference.Create(this), element, new { Value });

            ValueChanged.InvokeAsync(value);
        }
    }

    [Parameter]
    public EventCallback<List<string>> ValueChanged { get; set; }

    [JSInvokable]
    public void UpdateValue(List<string> Value)
    {
        this.Value = Value;
    }

    public async ValueTask DisposeAsync()
    {
        await module.InvokeVoidAsync("dispose", DotNetObjectReference.Create(this), element);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        module = await JS.InvokeAsync<IJSObjectReference>("import", "/_content/FluentCMS.Web.UI.Components/Components/Form/Autocomplete/Autocomplete.razor.js");

        await module.InvokeVoidAsync("initialize", DotNetObjectReference.Create(this), element);
    }
}



namespace FluentCMS.Web.UI.Components;

public partial class FormSelect
{
    
    private bool IsSelected(TItem item)
    {
        return (Value?.ToString() ?? String.Empty) == GetValue(item).ToString();
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

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        try
        {
            if (typeof(TValue) == typeof(bool))
            {
                if (bool.TryParse(value, out var @bool))
                {
                    result = (TValue)(object)@bool;
                    validationErrorMessage = null;
                    return true;
                }
                result = default!;
            }
            else if (typeof(TValue) == typeof(bool?))
            {
                if (string.IsNullOrEmpty(value))
                {
                    result = default!;
                    validationErrorMessage = null;
                    return true;
                }
                else
                {
                    if (bool.TryParse(value, out var @bool))
                    {
                        result = (TValue)(object)@bool;
                        validationErrorMessage = null;
                        return true;
                    }
                    result = default!;
                }
            }
            else if (BindConverter.TryConvertTo<TValue>(value, CultureInfo.CurrentCulture, out var parsedValue))
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

    protected override string? FormatValueAsString(TValue? value)
    {
        if (typeof(TValue) == typeof(bool))
        {
            return (bool)(object)value! ? "true" : "false";
        }
        else if (typeof(TValue) == typeof(bool?))
        {
            return value is not null && (bool)(object)value ? "true" : "false";
        }

        return base.FormatValueAsString(value);
    }
// }
