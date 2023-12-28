namespace FluentCMS.Web.Api.Models;

public class PluginDefinitionDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Type { get; set; } = default!;
}
