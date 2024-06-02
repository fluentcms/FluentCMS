using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormTextarea
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public int Rows { get; set; } = 5;

    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}
