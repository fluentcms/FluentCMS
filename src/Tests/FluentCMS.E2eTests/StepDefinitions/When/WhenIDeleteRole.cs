
namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Delete Role")]
    public async Task WhenIDeleteRoleAsync()
    {
        var rolesClient = context.Get<RoleClient>();
        var role = context.Get<RoleDetailResponseIApiResult>();
        var response = await rolesClient.DeleteAsync(role.Data.Id);
        context.Set(response);
    }
}
