namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Register")]
    public async Task WhenIRegister()
    {
        var accountClient = context.Get<AccountClient>();
        var credentials = context.Get<UserRegisterRequest>();
        var response = await accountClient.RegisterAsync(credentials);
        context.Set(response);
        context.Set(response.Errors);
    }
}
