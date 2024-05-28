namespace FluentCMS.Web.Api.Setup.Models;
internal class AdminTemplate
{
    public Site Site { get; set; } = default!;
    public List<LayoutTemplate> Layouts { get; set; } = [];
    public List<PluginDefinitionTemplate> PluginDefinitions { get; set; } = [];
    public List<PageTemplate> Pages { get; set; } = [];
    public List<Role> Roles { get; set; } = [];
}
