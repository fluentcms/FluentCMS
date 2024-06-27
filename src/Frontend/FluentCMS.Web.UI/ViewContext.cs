namespace FluentCMS.Web.UI;

public class ViewContext
{
    public LayoutDetailResponse Layout { get; set; } = default!;
    public PageFullDetailResponse Page { get; set; } = default!;
    public ViewType Type { get; set; } = ViewType.Default;
    public SiteDetailResponse Site { get; set; } = default!;
    public UserLoginResponse UserLogin { get; set; } = default!;
    public PluginDetailResponse? Plugin { get; set; }
    public string? PluginViewName { get; set; }
    public Guid? PluginId { get; set; }
}

public enum ViewType
{
    Default,
    PluginEdit,
    PageEdit,
    PagePreview
}
