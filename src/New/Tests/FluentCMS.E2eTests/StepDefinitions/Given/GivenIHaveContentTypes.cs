
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have {int} ContentTypes")]
    public async Task GivenIHaveContentTypesAsync(int count)
    {
        var contentTypeClient = context.Get<ContentTypeClient>();

        var app = context.Get<AppDetailResponseIApiResult>();

        var contentTypesToBeCreated = Enumerable.Range(1, count)
            .Select(x => new ContentTypeCreateRequest()
            {
                Title = $"DummyContentType{x}",
                Description = $"DummyContentType{x} description",
                Slug = $"dummy-content-type-{x}",
            });

        // iterate and create contentTypesToBeCreated
        foreach (var contentType in contentTypesToBeCreated)
        {
            await contentTypeClient.CreateAsync(app.Data.Slug, contentType);
        }
    }
}
