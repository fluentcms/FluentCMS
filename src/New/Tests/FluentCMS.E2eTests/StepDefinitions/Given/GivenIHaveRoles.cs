namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have {int} Roles")]
    public void GivenIHaveRoles(int count)
    {
        var rolesToCreate = Enumerable.Range(1, count).Select(x => new RoleCreateRequest()
        {
            Name = $"DummyRole{x}",
            Description = $"DummyRole{x} description"
        }).ToList();
        context.Set(rolesToCreate);
    }
}
