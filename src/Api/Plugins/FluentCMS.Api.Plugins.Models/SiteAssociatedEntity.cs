namespace FluentCMS.Api.Plugins.Models;

public interface ISiteAssociatedEntity : IAuditableEntity
{
    Guid SiteId { get; set; }
}

public abstract class SiteAssociatedEntity : AuditableEntity, ISiteAssociatedEntity
{
    public Guid SiteId { get; set; }
}
