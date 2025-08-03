namespace FluentCMS.Web.Api.Models;

public class PluginDefinitionDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string? Icon { get; set; } = default!;
    public string Assembly { get; set; } = default!;
    public string? Description { get; set; }
    public List<PluginDefinitionType> Types { get; set; } = [];
    public List<string> Stylesheets { get; set; } = [];
    public bool Locked { get; set; } = false;
}
