namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I delete a field")]
    public async void WhenIDeleteAField()
    {
        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var client = context.Get<ContentTypeClient>();

        var deleteResult = await client.DeleteFieldAsync(
                    contentType.Data.Id,
                    "dummy-field-slug");

        context.Set(deleteResult);

    }
}
