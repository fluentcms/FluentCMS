using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Components;

public partial class FormSwitch
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public string? Text { get; set; }

    protected override bool TryParseValueFromString(string? value, out bool result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        throw new NotSupportedException();
    }
}
