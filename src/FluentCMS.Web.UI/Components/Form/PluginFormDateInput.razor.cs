using Microsoft.AspNetCore.Components.Forms;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Globalization;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormDateInput<TValue>
{
    [Parameter]
    public int Cols { get; set; } = 12;

    private const string DateFormat = "yyyy-MM-dd";                     
    private const string DateTimeLocalFormat = "yyyy-MM-ddTHH:mm:ss";   
    private const string MonthFormat = "yyyy-MM";                       
    private const string TimeFormat = "HH:mm:ss";                       

    private string _typeAttributeValue = default!;
    private string _format = default!;
    private string _parsingErrorMessage = default!;

    [Parameter]
    public InputDateType Type { get; set; } = InputDateType.Date;

    [Parameter]
    public string ParsingErrorMessage { get; set; } = string.Empty; 

    public PluginFormDateInput()
    {
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        if (type != typeof(DateTime) &&
            type != typeof(DateTimeOffset) &&
            type != typeof(DateOnly) &&
            type != typeof(TimeOnly))
        {
            throw new InvalidOperationException($"Unsupported {GetType()} type param '{type}'.");
        }
    }

    protected override void OnParametersSet()
    {
        (_typeAttributeValue, _format, var formatDescription) = Type switch
        {
            InputDateType.Date => ("date", DateFormat, "date"),
            InputDateType.DateTimeLocal => ("datetime-local", DateTimeLocalFormat, "date and time"),
            InputDateType.Month => ("month", MonthFormat, "year and month"),
            InputDateType.Time => ("time", TimeFormat, "time"),
            _ => throw new InvalidOperationException($"Unsupported {nameof(InputDateType)} '{Type}'.")
        };

        _parsingErrorMessage = string.IsNullOrEmpty(ParsingErrorMessage)
            ? $"The {{0}} field must be a {formatDescription}."
            : ParsingErrorMessage;
    }

    protected override string FormatValueAsString(TValue? value)
        => value switch
        {
            DateTime dateTimeValue => BindConverter.FormatValue(dateTimeValue, _format, CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffsetValue => BindConverter.FormatValue(dateTimeOffsetValue, _format, CultureInfo.InvariantCulture),
            DateOnly dateOnlyValue => BindConverter.FormatValue(dateOnlyValue, _format, CultureInfo.InvariantCulture),
            TimeOnly timeOnlyValue => BindConverter.FormatValue(timeOnlyValue, _format, CultureInfo.InvariantCulture),
            _ => string.Empty, // Handles null for Nullable<DateTime>, etc.
        };

    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (BindConverter.TryConvertTo(value, CultureInfo.InvariantCulture, out result))
        {
            Debug.Assert(result != null);
            validationErrorMessage = null;
            return true;
        }
        else
        {
            validationErrorMessage = string.Format(CultureInfo.InvariantCulture, _parsingErrorMessage, DisplayName ?? FieldIdentifier.FieldName);
            return false;
        }
    }
}
