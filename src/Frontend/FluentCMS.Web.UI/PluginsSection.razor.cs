namespace FluentCMS.Web.UI;

public partial class PluginsSection
{
    [Parameter]
    // this will be set while dynamically rendering the template
    public string Name { get; set; } = default!;

    [Inject]
    private ViewState ViewState { get; set; } = default!;
}
