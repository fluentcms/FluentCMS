using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormSelect<TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    private readonly bool _isMultipleSelect;

    public PluginFormSelect()
    {
        _isMultipleSelect = typeof(TValue).IsArray;
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

    private void SetCurrentValueAsStringArray(string?[]? value)
    {
        CurrentValue = BindConverter.TryConvertTo<TValue>(value, CultureInfo.CurrentCulture, out var result)
            ? result
            : default;
    }
}
