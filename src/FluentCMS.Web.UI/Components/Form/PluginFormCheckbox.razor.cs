using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginFormCheckbox
{
    [Parameter]
    public int Cols { get; set; } = 12;

    [Parameter]
    public string? Text { get; set; }

    public PluginFormCheckbox()
    {
        Id ??= Guid.NewGuid().ToString();
    }

    protected override bool TryParseValueFromString(string? value, out bool result, [NotNullWhen(false)] out string? validationErrorMessage)
    {
        throw new NotSupportedException();
    }
}
