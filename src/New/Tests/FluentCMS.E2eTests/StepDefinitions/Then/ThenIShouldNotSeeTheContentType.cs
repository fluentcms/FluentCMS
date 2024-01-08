
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should not see the content type")]
    public async Task ThenIShouldNotSeeTheContentTypeAsync()
    {
        var createResponse = context.Get<ContentTypeResponseIApiResult>();

        var client = context.Get<ContentTypeClient>();

        var app = context.Get<AppDetailResponseIApiResult>();

        var result = await client.GetAllAsync(app.Data.Slug!);

        result.Data.ShouldNotContain(x => x.Slug == createResponse.Data.Slug);
    }
}
