namespace FluentCMS.Api.Core.Models;

public interface ISiteAssociatedEntity : IAuditableEntity
{
    [ConcurrencyCheck]
    Guid SiteId { get; set; }
}

public abstract class SiteAssociatedEntity : AuditableEntity, ISiteAssociatedEntity
{
    public Guid SiteId { get; set; }
}
