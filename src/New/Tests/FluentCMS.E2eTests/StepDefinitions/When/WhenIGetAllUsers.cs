namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I get all users")]
    public async Task WhenIGetAllUsersAsync()
    {
        var userClient = context.Get<UserClient>();
        var users = await userClient.GetAllAsync();
        context.Set(users);
    }
}
