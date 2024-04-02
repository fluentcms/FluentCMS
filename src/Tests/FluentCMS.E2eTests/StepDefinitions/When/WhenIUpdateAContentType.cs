
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I update a content type")]
    public async Task WhenIUpdateAContentTypeAsync()
    {
        var updateRequest = context.Get<ContentTypeUpdateRequest>();
        var client = context.Get<ContentTypeClient>();
        var result = await client.UpdateAsync(updateRequest);
        context.Set(result);
    }
}
