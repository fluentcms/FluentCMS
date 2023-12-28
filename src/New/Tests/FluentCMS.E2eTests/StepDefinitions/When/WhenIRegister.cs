using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Register")]
    public async Task WhenIRegister()
    {
        var accountClient = context.Get<AccountClient>();
        var credentials = context.Get<UserRegisterRequest>();
        var registerBody = new UserRegisterRequest()
        {
            Username = credentials.Username,
            Password = credentials.Password,
            Email = credentials.Email
        };
        var response = await accountClient.RegisterAsync(registerBody);
        context.Set(response);
        context.Set(response.Errors);
    }
}
