using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormTextInput
{
    [Parameter]
    public int Cols { get; set; } = 12;

    protected override bool TryParseValueFromString(string? value, out string? result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        result = value;
        validationErrorMessage = null;
        return true;
    }
}
