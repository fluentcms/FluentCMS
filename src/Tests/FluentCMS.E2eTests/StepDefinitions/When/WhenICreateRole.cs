
namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Create Role")]
    public async Task WhenICreateRoleAsync()
    {
        var rolesClient = context.Get<RoleClient>();
        var roleCreateRequest = context.Get<RoleCreateRequest>();
        var response = await rolesClient.CreateAsync(roleCreateRequest);
        context.Set(response);
    }
}
