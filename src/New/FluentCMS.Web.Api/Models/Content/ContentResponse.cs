namespace FluentCMS.Web.Api.Models;

public class ContentResponse : BaseAppAssociatedResponse
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object> Value { get; set; } = [];
}
