namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Reset Setup")]
    public async Task WhenIResetSetup()
    {
        await context.Get<SetupClient>().ResetAsync();
    }
}
