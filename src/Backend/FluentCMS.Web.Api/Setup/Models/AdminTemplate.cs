using FluentCMS.Entities.Sites;

namespace FluentCMS.Web.Api.Setup.Models;
internal class AdminTemplate
{
    public GlobalSettings GlobalSettings { get; set; } = default!;
    public SiteTemplate Site { get; set; } = default!;
    public List<LayoutTemplate> Layouts { get; set; } = [];
    public List<PluginDefinitionTemplate> PluginDefinitions { get; set; } = [];
    public List<PageTemplate> Pages { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
    public List<ContentTypeTemplate> ContentTypes { get; set; } = [];
}
