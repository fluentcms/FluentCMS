using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Update Role to")]
    public async Task WhenIUpdateRoleTo(Table table)
    {
        var updateRequest = table.CreateInstance<RoleUpdateRequest>();
        var roleToBeUpdated = context.Get<RoleDetailResponseIApiResult>();
        updateRequest.Id = roleToBeUpdated.Data.Id;
        var app = context.Get<AppDetailResponseIApiResult>();
        var rolesClient = context.Get<RoleClient>();
        var response = await rolesClient.UpdateAsync(app.Data.Slug!, updateRequest);
        context.Set(response);

    }
}
