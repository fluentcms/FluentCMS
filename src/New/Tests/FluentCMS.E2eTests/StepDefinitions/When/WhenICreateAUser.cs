namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I create a user")]
    public async Task WhenICreateAUserAsync()
    {
        var userClient = context.Get<UserClient>();
        var credentials = context.Get<UserRegisterRequest>();
        var createUserRequest = new UserCreateRequest()
        {
            Email = credentials.Email,
            Username = credentials.Username,
            Password = credentials.Password,
        };
        var response = await userClient.CreateAsync(createUserRequest);
        context.Set(response);
    }
}
