namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Role Update Result should be Success")]
    public void ThenRoleUpdateResultShouldBeSuccess()
    {
        var result = context.Get<RoleDetailResponseIApiResult>();
        result.Errors.ShouldBeEmpty();
    }
}
