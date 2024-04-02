
namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Get All Roles")]
    public async Task WhenIGetAllRolesAsync()
    {
        var rolesClient = context.Get<RoleClient>();
        var response = await rolesClient.GetAllAsync();
        context.Set(response);
    }
}
