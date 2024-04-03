
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I delete a content type")]
    public async Task WhenIDeleteAContentTypeAsync()
    {
        // fetch content type from previous steps
        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        // delete content type
        var contentTypeClient = context.Get<ContentTypeClient>();

        var deleteResult = await contentTypeClient.DeleteAsync(contentType.Data.Id);

        deleteResult.Data.ShouldBeTrue();

        context.Set(deleteResult);
    }
}
