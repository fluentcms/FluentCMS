using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Create App")]
    public async Task WhenICreateAppAsync()
    {
        var appCreateRequest = context.Get<AppCreateRequest>();
        var appClient = context.Get<AppClient>();
        var result = await appClient.CreateAsync(appCreateRequest);
        context.Set(result);
    }
}
