using FluentCMS.Web.UI.Plugins;

namespace FluentCMS.Web.UI.Components;
public partial class ErrorDisplay
{
    [CascadingParameter]
    public ErrorContext ErrorContext { get; set; } = default!;
}
