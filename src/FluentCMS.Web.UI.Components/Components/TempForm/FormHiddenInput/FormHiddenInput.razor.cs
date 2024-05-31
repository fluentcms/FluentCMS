using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormHiddenInput<TValue>
{
    protected override bool TryParseValueFromString(string? value, [MaybeNullWhen(false)] out TValue result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        if (string.IsNullOrEmpty(value))
        {
            result = default!;
            validationErrorMessage = null;
            return true;
        }

        if (typeof(TValue) == typeof(string))
        {
            result = (TValue)(object)value;
            validationErrorMessage = null;
            return true;
        }

        if (typeof(TValue) == typeof(Guid))
        {
            result = (TValue)(object)Guid.Parse(value);
            validationErrorMessage = null;
            return true;
        }

        throw new InvalidOperationException($"The type '{typeof(TValue)}' is not a supported type.");
    }
}
