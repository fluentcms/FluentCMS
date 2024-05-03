namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When(@"I ChangePassword")]
    public async Task WhenIChangePassword()
    {
        var accountClient = context.Get<AccountClient>();
        var changePasswordRequest = context.Get<UserChangePasswordRequest>();
        try
        {
            var result = await accountClient.ChangePasswordAsync(changePasswordRequest);
            context.Set(result);
        }
        catch (ApiClientException e)
        {
            context.Set(e);
        }

    }
}
