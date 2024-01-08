
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I get all content types")]
    public async Task WhenIGetAllContentTypesAsync()
    {
        var contentTypeService = context.Get<ContentTypeClient>();

        var app = context.Get<AppResponseIApiResult>();

        var contentTypes = await contentTypeService.GetAllAsync(app.Data.Slug!);

        context.Set(contentTypes);
    }
}
