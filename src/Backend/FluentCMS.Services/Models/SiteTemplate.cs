namespace FluentCMS.Services.Models;

public class SiteTemplate
{
    public Guid Id { get; set; }
    public string Url { get; set; } = default!;
    public string Template { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<Layout> Layouts { get; set; } = [];
    public List<Block> Blocks { get; set; } = [];
    public List<ContentTypeTemplate> ContentTypes { get; set; } = [];
    public string Layout { get; set; } = default!;
    public string EditLayout { get; set; } = default!;
    public string DetailLayout { get; set; } = default!;
    public List<PageTemplate> Pages { get; set; } = [];
    public List<FileTemplate> Files { get; set; } = [];
    public List<FolderTemplate> Folders { get; set; } = [];
    public List<string> AdminRoles { get; set; } = [];
    public List<string> ContributorRoles { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
    public Dictionary<string, string> Settings { get; set; } = [];
}
