using FluentCMS.Entities;

namespace FluentCMS.Services;
public interface ISecurityContext
{
    bool HasAccess<TEntity>(TEntity entity, AccessType accessType);

}

public class Access : AuditEntity
{
    public Guid EntityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public List<EntityRole> Roles { get; set; } = [];
}

public class EntityRole
{
    public AccessType AccessType { get; set; }
    public Guid RoleId { get; set; }
}

public enum AccessType
{
    Read = 0,
    Create = 1,
    Update = 2,
    Delete = 3
}