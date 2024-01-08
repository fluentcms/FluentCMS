
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
        var app = context.Get<AppResponseIApiResult>();
        foreach (var role in rolesToCreate)
        {
            await rolesClient.CreateAsync(app.Data.Slug!, role);
        }
        context.Set(rolesToCreate);
    }
}
