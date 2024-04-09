
namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have {int} Roles")]
    public async Task GivenIHaveRolesAsync(int count)
    {
        var rolesToCreate = Enumerable.Range(1, count).Select(x => new RoleCreateRequest()
        {
            Name = $"DummyRole{x}",
            Description = $"DummyRole{x} description"
        }).ToList();
        var rolesClient = context.Get<RoleClient>();
        foreach (var role in rolesToCreate)
        {
            await rolesClient.CreateAsync(role);
        }
        context.Set(rolesToCreate);
    }
}
