namespace FluentCMS.Web.Api.Setup;
internal class AdminTemplate
{
    public Site Site { get; set; } = default!;
    public List<Layout> Layouts { get; set; } = [];
    public List<PluginDefinition> PluginDefinitions { get; set; } = [];
    public List<PageTemplate> Pages { get; set; } = [];
}
