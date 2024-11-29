using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FluentCMS.Web.UI.Components;

public partial class FormNumberInput<TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public NumberInputType Type { get; set; } = NumberInputType.Number;

    private static readonly string _stepAttributeValue = GetStepAttributeValue();

    private static string GetStepAttributeValue()
    {
        // Unwrap Nullable<T>, because InputBase already deals with the Nullable aspect
        // of it for us. We will only get asked to parse the T for nonempty inputs.
        var targetType = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        if (targetType == typeof(int) ||
            targetType == typeof(long) ||
            targetType == typeof(short) ||
            targetType == typeof(float) ||
            targetType == typeof(double) ||
            targetType == typeof(decimal))
        {
            return "any";
        }
        else
        {
            throw new InvalidOperationException($"The type '{targetType}' is not a supported numeric type.");
        }
    }

    [Parameter]
    public string ParsingErrorMessage { get; set; } = "The {0} field must be a number.";

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (BindConverter.TryConvertTo<TValue>(value, CultureInfo.InvariantCulture, out result))
        {
            validationErrorMessage = null;
            return true;
        }
        else
        {
            validationErrorMessage = string.Format(CultureInfo.InvariantCulture, ParsingErrorMessage, DisplayName ?? FieldIdentifier.FieldName);
            return false;
        }
    }

    protected override string? FormatValueAsString(TValue? value)
    {
        // Avoiding a cast to IFormattable to avoid boxing.
        switch (value)
        {
            case null:
                return null;

            case int @int:
                return BindConverter.FormatValue(@int, CultureInfo.InvariantCulture);

            case long @long:
                return BindConverter.FormatValue(@long, CultureInfo.InvariantCulture);

            case short @short:
                return BindConverter.FormatValue(@short, CultureInfo.InvariantCulture);

            case float @float:
                return BindConverter.FormatValue(@float, CultureInfo.InvariantCulture);

            case double @double:
                return BindConverter.FormatValue(@double, CultureInfo.InvariantCulture);

            case decimal @decimal:
                return BindConverter.FormatValue(@decimal, CultureInfo.InvariantCulture);

            default:
                throw new InvalidOperationException($"Unsupported type {value.GetType()}");
        }
    }
}
