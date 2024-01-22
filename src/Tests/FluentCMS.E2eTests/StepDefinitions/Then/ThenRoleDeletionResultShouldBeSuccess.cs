namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Role Deletion Result should be Success")]
    public void ThenRoleDeletionResultShouldBeSuccess()
    {
        var result = context.Get<BooleanIApiResult>();
        result.Data.ShouldBeTrue();
    }
}
