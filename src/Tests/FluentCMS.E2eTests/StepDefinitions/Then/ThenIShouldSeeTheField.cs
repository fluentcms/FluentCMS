namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should see the field")]
    public async Task ThenIShouldSeeTheFieldAsync()
    {
        var client = context.Get<ContentTypeClient>();
        var app = context.Get<AppDetailResponseIApiResult>();

        // fetch all content types
        var result = await client.GetAllAsync(app.Data.Slug!);

        // find the content type
        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var updatedContentType = result.Data!.Single(x => x.Id == contentType.Data.Id);

        // find the field
        var field = updatedContentType.Fields!.SingleOrDefault(x => x.Slug == contentType.Data.Fields!.First().Slug);

        // assert
        field.ShouldNotBeNull();

    }
}
