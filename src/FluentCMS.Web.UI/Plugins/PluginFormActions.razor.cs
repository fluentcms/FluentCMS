namespace FluentCMS.Web.UI.Plugins;

public partial class PluginFormActions
{
    [Inject]
    protected NavigationManager NavigationManager { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected virtual string GetBackUrl()
    {
        return new Uri(NavigationManager.Uri).LocalPath;
    }
}
