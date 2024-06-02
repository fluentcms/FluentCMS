namespace FluentCMS.Web.Api.Setup.Models;
internal class PageTemplate
{
    public string Path { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Layout { get; set; } = default!;
    public List<PageTemplate> Children { get; set; } = [];
    public List<PluginTemplate> Plugins { get; set; } = [];
}
