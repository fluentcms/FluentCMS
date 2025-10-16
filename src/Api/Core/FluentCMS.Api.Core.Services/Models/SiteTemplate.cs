namespace FluentCMS.Api.Core.Services.Models;

public class SiteTemplate
{
    public string Url { get; set; } = default!;
    public string TemplateName { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<Layout> Layouts { get; set; } = [];
    public string LayoutName { get; set; } = default!;
    public string EditLayoutName { get; set; } = default!;
    public string DetailLayoutName { get; set; } = default!;
    public List<PageTemplate> Pages { get; set; } = [];
    public List<FileTemplate> Files { get; set; } = [];
    public List<FolderTemplate> Folders { get; set; } = [];
    public List<string> AdminRoles { get; set; } = [];
    public List<string> ContributorRoles { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
}
