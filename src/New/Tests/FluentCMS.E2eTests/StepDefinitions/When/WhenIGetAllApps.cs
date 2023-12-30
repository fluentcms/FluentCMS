using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Get All Apps")]
    public async Task WhenIGetAllAppsAsync()
    {
        var appClient = context.Get<AppClient>();
        var apps = await appClient.GetAllAsync();
        context.Set(apps);
    }
}
