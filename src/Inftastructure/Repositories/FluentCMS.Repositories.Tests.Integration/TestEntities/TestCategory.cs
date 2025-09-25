using FluentCMS;

namespace FluentCMS.Repositories.Tests.Integration.TestEntities;

public class TestCategory : IEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
