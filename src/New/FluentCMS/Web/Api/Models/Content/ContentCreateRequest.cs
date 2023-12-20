namespace FluentCMS.Web.Api.Models;
public class ContentCreateRequest
{
    public required Guid SiteId { get; set; }
    public required Dictionary<string, object?> Value { get; set; }
}
