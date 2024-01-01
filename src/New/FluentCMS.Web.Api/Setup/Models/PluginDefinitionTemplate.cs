namespace FluentCMS.Web.Api.Setup.Models;
internal class PluginDefinitionTemplate
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Type { get; set; } = default!;
}
