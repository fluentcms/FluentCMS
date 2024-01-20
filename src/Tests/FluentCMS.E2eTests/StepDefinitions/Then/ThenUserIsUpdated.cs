namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{

    [Then("user is updated")]
    public void ThenUserIsUpdated()
    {
        var user = context.Get<UserDetailResponseIApiResult>();
        user.Errors.ShouldBeEmpty();
        user.Data.ShouldNotBeNull();
        user.Data.Email.ShouldBe("UpdatedDummyEmail@localhost");
    }
}
