namespace FluentCMS.Repositories.EFCore.DbModels;

public interface ISiteAssociatedEntityModel : IAuditableEntityModel
{
    Guid SiteId { get; set; }
}

public abstract class SiteAssociatedEntityModel : AuditableEntityModel, ISiteAssociatedEntityModel
{
    public Guid SiteId { get; set; }
    public SiteModel Site { get; set; } = default!; // Navigation property
}
