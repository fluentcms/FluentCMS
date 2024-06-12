using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluentCMS.Web.UI.Components;

public partial class FormRadioGroup<TItem, TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public ICollection<TItem> Data { get; set; } = [];

    [Parameter]
    public string? TextField { get; set; }

    [Parameter]
    public string? ValueField { get; set; }

    [Parameter]
    [CSSProperty]
    public bool Vertical { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public EventCallback<TValue> OnChange { get; set; }

    private bool IsChecked(TItem item)
    {
        return (Value?.ToString() ?? string.Empty) == GetValue(item).ToString();
    }

    private string? GetText(TItem item)
    {
        if (string.IsNullOrEmpty(TextField))
        {
            return item!.ToString();
        }
        return (string?)item!.GetType().GetProperty(TextField)?.GetValue(item);
    }

    private TValue GetValue(TItem item)
    {
        TValue? result;

        if (string.IsNullOrEmpty(ValueField))
            result = (TValue)(object)item!;
        else
            result = (TValue?)item?.GetType().GetProperty(ValueField)?.GetValue(item);

        if (result == null)
            throw new InvalidOperationException($"The {ValueField} field is not valid.");

        return result;
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

    protected override string? FormatValueAsString(TValue? value)
    {
        return base.FormatValueAsString(value);
    }

    public Task HandleChange(ChangeEventArgs args, TValue value)
    {
        Value = value;

        OnChange.InvokeAsync(Value);
        ValueChanged.InvokeAsync(Value);
        return Task.CompletedTask;
    }
}
