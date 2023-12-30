using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Update App")]
    public async Task WhenIUpdateAppAsync()
    {
        var appUpdateRequest = context.Get<AppUpdateRequest>();
        
        var appClient = context.Get<AppClient>();
        var result = await appClient.UpdateAsync(appUpdateRequest);
        context.Set(result);
    }
}
