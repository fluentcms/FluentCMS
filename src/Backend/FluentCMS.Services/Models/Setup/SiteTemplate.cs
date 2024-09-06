namespace FluentCMS.Services.Models.Setup;

public class SiteTemplate
{
    public Guid Id { get; set; }
    public string Url { get; set; } = default!;
    public string Template { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public List<Layout> Layouts { get; set; } = [];
    public string Layout { get; set; } = default!;
    public string EditLayout { get; set; } = default!;
    public string DetailLayout { get; set; } = default!;
    public List<PageTemplate> Pages { get; set; } = [];
}
