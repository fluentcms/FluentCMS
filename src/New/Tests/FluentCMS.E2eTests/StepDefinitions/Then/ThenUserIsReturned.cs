namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("user is returned")]
    public void ThenUserIsReturned()
    {
        var user = context.Get<UserResponseIApiResult>();
        user.Errors.ShouldBeEmpty();
        user.Data.ShouldNotBeNull();
        user.Data.Id.ShouldNotBe(default);
    }
}
