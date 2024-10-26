namespace FluentCMS.Services.Models;

public class PageTemplate
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Path { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Layout { get; set; } = default!;
    public string? DetailLayout { get; set; } = default!;
    public List<PageTemplate> Children { get; set; } = [];
    public List<PluginTemplate> Plugins { get; set; } = [];
    public bool Locked { get; set; } = false;
    public List<string> AdminRoles { get; set; } = [];
    public List<string> ContributorRoles { get; set; } = [];
    public List<string> ViewRoles { get; set; } = [];
}
