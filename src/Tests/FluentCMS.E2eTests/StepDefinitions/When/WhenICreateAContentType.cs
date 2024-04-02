
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I create a content type")]
    public async Task WhenICreateAContentTypeAsync()
    {
        var request = context.Get<ContentTypeCreateRequest>();

        var client = context.Get<ContentTypeClient>();

        var result = await client.CreateAsync(request);

        context.Set(result);
    }
}
