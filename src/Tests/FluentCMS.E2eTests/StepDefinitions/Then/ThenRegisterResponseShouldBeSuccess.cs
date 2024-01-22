namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Register Response Should be success")]
    public void ThenRegisterResponseShouldBeSuccess()
    {
        var registerResponse = context.Get<UserDetailResponseIApiResult>();
        registerResponse.Errors.ShouldBeEmpty();
    }
}
