namespace FluentCMS.Web.Plugins.Admin.FileManagement;

public class FolderBreadcrumbItemType
{
    public string? Href { get; set; }
    public string? Title { get; set; }
    public IconName Icon { get; set; } = IconName.Default;
};