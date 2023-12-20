namespace FluentCMS.Web.Api.Models;

public class BaseAuditableResponse
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime? CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime ModifiedAt { get; set; }
}

public class BaseAppAssociatedResponse : BaseAuditableResponse
{
    public Guid AppId { get; set; }
}
