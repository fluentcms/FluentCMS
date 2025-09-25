namespace FluentCMS.Repositories.Tests.Integration.TestEntities;

public class TestUser : IEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Age { get; set; }
}
