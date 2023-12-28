using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Start Setup")]
    public async Task WhenIStartSetup()
    {
        await context.Get<SetupClient>().StartAsync();
    }
}
