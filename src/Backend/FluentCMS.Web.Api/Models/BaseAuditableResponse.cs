namespace FluentCMS.Web.Api.Models;

public abstract class BaseAuditableResponse
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
}

public abstract class BaseSiteAssociatedResponse : BaseAuditableResponse
{
    public Guid SiteId { get; set; }
}
