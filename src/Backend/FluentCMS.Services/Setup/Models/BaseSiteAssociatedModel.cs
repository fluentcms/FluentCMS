namespace FluentCMS.Services.Setup.Models;

public class BaseSiteAssociatedModel : BaseAuditableModel
{
    public Guid SiteId { get; set; }
}
