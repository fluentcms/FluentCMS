namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I get a user with id")]
    public async Task WhenIGetAUserWithIdAsync()
    {
        var userCreateResponse = context.Get<UserDetailResponseIApiResult>();
        var userId = userCreateResponse.Data.Id;
        var userClient = context.Get<UserClient>();
        var user = await userClient.GetAsync(userId);
        context.Set(user);
    }
}
