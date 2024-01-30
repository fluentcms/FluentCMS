
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should see the content type")]
    public async Task ThenIShouldSeeTheContentTypeAsync()
    {
        var createResponse = context.Get<ContentTypeDetailResponseIApiResult>();

        var client = context.Get<ContentTypeClient>();

        var app = context.Get<AppDetailResponseIApiResult>();

        var result = await client.GetAllAsync(app.Data.Slug!);

        result.Data!.ShouldContain(x => x.Slug == createResponse.Data.Slug);
    }
}
