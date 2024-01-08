namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I delete a field")]
    public void WhenIDeleteAField()
    {
        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var app = context.Get<AppDetailResponseIApiResult>();

        var client = context.Get<ContentTypeClient>();

        var deleteResult = client.DeleteFieldAsync(
                    app.Data.Slug!,
                    contentType.Data.Id,
                    "test-field");

    }
}
