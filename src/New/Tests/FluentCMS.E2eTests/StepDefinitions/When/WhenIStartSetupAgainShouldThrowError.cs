namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Start Setup again should throw Error")]
    public async Task WhenIStartSetupAgainShouldThrowError()
    {
        var setupClient = context.Get<SetupClient>();
        var startupBody = context.Get<SetupRequest>();
        var e = await (setupClient.StartAsync(startupBody).ShouldThrowAsync<ApiClientException>());
        // TODO: fetch and check error message
    }
}
