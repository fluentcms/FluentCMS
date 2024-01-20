namespace FluentCMS.Entities;

public interface IAppAssociatedEntity : IAuditableEntity
{
    Guid AppId { get; set; }
}

public abstract class AppAssociatedEntity : AuditableEntity, IAppAssociatedEntity
{
    public Guid AppId { get; set; }
}

