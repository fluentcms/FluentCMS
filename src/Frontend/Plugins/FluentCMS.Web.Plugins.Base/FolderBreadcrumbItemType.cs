namespace FluentCMS.Web.Plugins;

public class FolderBreadcrumbItemType
{
    public Guid? Id { get; set; }
    public string? Title { get; set; }
    public IconName Icon { get; set; } = IconName.Default;
};