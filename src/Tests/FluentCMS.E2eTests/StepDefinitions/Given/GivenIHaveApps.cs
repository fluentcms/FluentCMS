namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have {int} Apps")]
    public async Task GivenIHaveAppsAsync(int count)
    {
        var appsToCreate = Enumerable.Range(1, count).Select(x => new AppCreateRequest()
        {
            Title = $"DummyApp{x}",
            Description = $"DummyApp{x} description",
            Slug = $"dummy-app-{x}"
        });
        var appClient = context.Get<AppClient>();
        foreach (var app in appsToCreate)
        {
            await appClient.CreateAsync(app);
        }
    }
}
