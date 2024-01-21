
namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Get All Roles")]
    public async Task WhenIGetAllRolesAsync()
    {
        var app = context.Get<AppDetailResponseIApiResult>();
        var rolesClient = context.Get<RoleClient>();
        var response = await rolesClient.GetAllAsync(app.Data.Slug!);
        context.Set(response);
    }
}
