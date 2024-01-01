using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Start Setup")]
    public async Task WhenIStartSetup(Table table)
    {
        var body = table.CreateInstance<SetupRequest>();
        context.Set<SetupRequest>(body);
        await context.Get<SetupClient>().StartAsync(body);
    }
}
