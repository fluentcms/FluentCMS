using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories;

public class Entity : IEntity
{
    public virtual Guid Id { get; set; }
}
