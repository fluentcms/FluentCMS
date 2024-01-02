namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Delete App")]
    public async Task WhenIDeleteAppAsync()
    {
        var appId = context.Get<Guid>("appId");
        var appClient = context.Get<AppClient>();
        var result = await appClient.DeleteAsync(appId);
        context.Set(result);
    }
}
