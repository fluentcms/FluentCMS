namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresContentType", Order = 50)]
    public async Task RequiresContentType()
    {
        var table = new Table("field", "value");
        table.AddRow("title", "test");
        table.AddRow("description", "test description");
        table.AddRow("slug", "test-slug");

        GivenIHaveAService("ContentTypeClient");
        GivenIHaveAContentTypeCreateRequest(table);
        await WhenICreateAContentTypeAsync();
        await ThenIShouldSeeTheContentTypeAsync();
    }
}
