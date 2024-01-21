namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Role Creation Result should be Success")]
    public void ThenRoleCreationResultShouldBeSuccess()
    {
        var result = context.Get<RoleDetailResponseIApiResult>();
        result.Errors.ShouldBeEmpty();
    }
}
