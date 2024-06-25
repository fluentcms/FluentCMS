namespace FluentCMS.Web.Api.Models;

public class PluginContentDetailResponse : BaseAuditableResponse
{
    public Guid SiteId { get; set; }
    public Guid PluginId { get; set; }
    public string Type { get; set; } = default!;
    public Dictionary<string, object?> Data { get; set; } = [];
}
