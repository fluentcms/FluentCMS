namespace FluentCMS.Api.Core.Models;

public interface ISiteAssociatedEntity : IAuditableEntity
{
    Guid SiteId { get; set; }
}

public abstract class SiteAssociatedEntity : AuditableEntity, ISiteAssociatedEntity
{
    [ConcurrencyCheck]
    public Guid SiteId { get; set; }
}
