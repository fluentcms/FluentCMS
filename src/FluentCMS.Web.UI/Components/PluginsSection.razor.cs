namespace FluentCMS.Web.UI.Plugins.Components;

public partial class PluginsSection
{
    [Parameter]
    // this will be set while dynamically rendering the template
    public string Name { get; set; } = default!;

    [CascadingParameter]
    private PageFullDetailResponse? Page { get; set; }
}
