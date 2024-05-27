using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormCheckbox
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public bool Dence { get; set; }

    [Parameter]
    public string? Text { get; set; }

    protected override bool TryParseValueFromString(string? value, out bool result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        throw new NotSupportedException();
    }
}
