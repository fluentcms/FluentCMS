namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I delete a field")]
    public void WhenIDeleteAField()
    {
        var contentType = context.Get<ContentTypeResponseIApiResult>();

        var app = context.Get<AppResponseIApiResult>();

        var client = context.Get<ContentTypeClient>();

        var deleteResult = client.DeleteFieldAsync(
                    app.Data.Slug!,
                    contentType.Data.Id,
                    "test-field");

    }
}
