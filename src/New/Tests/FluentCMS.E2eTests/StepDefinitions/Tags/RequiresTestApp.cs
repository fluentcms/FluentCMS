namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresTestApp", Order = 30)]
    public async Task RequiresTestApp()
    {
        var table = new Table("field", "value");
        table.AddRow("slug", "test");
        table.AddRow("title", "test");
        table.AddRow("description", "test");

        context.Set("test", "appSlug");

        GivenIHaveAService("AppClient");
        GivenIHaveAppCreateRequest(table);
        await WhenICreateAppAsync();
        ThenAppCreateResultShouldBeSuccess();
    }
}
