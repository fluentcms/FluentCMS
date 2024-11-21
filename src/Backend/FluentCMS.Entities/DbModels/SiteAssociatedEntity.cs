namespace FluentCMS.Repositories.EFCore.DbModels;

public interface ISiteAssociatedEntity : IAuditableEntity
{
    Guid SiteId { get; set; }
}

public abstract class SiteAssociatedEntity : AuditableEntity, ISiteAssociatedEntity
{
    public Guid SiteId { get; set; }
}
