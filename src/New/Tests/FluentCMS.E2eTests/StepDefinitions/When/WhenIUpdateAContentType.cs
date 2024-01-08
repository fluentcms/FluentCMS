
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I update a content type")]
    public async Task WhenIUpdateAContentTypeAsync()
    {
        var updateRequest = context.Get<ContentTypeUpdateRequest>();
        var client = context.Get<ContentTypeClient>();
        var app = context.Get<AppResponseIApiResult>();
        var result = await client.UpdateAsync(app.Data.Slug!, updateRequest);
        context.Set(result);
    }
}
