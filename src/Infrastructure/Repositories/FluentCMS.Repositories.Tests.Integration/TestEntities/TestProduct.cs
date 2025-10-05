using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.Tests.Integration.TestEntities;

public class TestProduct : IEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public bool IsActive { get; set; }
}
