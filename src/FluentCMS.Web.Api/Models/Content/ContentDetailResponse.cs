namespace FluentCMS.Web.Api.Models;

public class ContentDetailResponse : BaseAppAssociatedResponse
{
    public Guid TypeId { get; set; }
    public Dictionary<string, object> Value { get; set; } = [];
}
