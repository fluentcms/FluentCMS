using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.EntityFramework;

public class Entity : IEntity
{
    public virtual Guid Id { get; set; }
}
