namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("all users are returned")]
    public void ThenAllUsersAreReturned()
    {
        var users = context.Get<UserDetailResponseIApiPagingResult>();
        users.Errors.ShouldBeEmpty();
        users.Data.ShouldNotBeNull();
        users.Data.ShouldNotBeEmpty();
        users.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
    }
}
