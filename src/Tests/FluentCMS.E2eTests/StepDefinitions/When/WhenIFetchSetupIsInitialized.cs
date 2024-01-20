namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Fetch Setup IsInitialized")]
    public async Task WhenIFetchSetupIsInitialized()
    {
        var setupClient = context.Get<SetupClient>();
        var status = await setupClient.IsInitializedAsync();
        context[_isInitializedKey] = status;
        context.Set(status.Errors);
    }
}
