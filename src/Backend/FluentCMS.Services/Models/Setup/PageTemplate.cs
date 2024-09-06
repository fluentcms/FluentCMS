namespace FluentCMS.Services.Models.Setup;

public class PageTemplate
{
    public Guid Id { get; set; }
    public Guid SiteId { get; set; }
    public string Path { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Layout { get; set; } = default!;
    public string? DetailLayout { get; set; } = default!;
    public string? EditLayout { get; set; } = default!;
    public List<PageTemplate> Children { get; set; } = [];
    public List<PluginTemplate> Plugins { get; set; } = [];
    public bool Locked { get; set; } = false;
}
