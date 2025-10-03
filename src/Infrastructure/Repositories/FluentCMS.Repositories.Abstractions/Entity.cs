using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Repositories.Abstractions;

public interface IEntity
{
    Guid Id { get; set; }
}

public class Entity : IEntity
{
    [Key]
    public Guid Id { get; set; }
}
