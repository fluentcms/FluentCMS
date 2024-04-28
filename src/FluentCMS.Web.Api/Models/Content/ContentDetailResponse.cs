namespace FluentCMS.Web.Api.Models;

public class ContentDetailResponse : BaseAppAssociatedResponse
{
    [Required]
    public Guid TypeId { get; set; }

    [Required]
    public Dictionary<string, object> Value { get; set; } = [];

}
