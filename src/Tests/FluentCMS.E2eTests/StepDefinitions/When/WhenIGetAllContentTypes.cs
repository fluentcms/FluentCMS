
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I get all content types")]
    public async Task WhenIGetAllContentTypesAsync()
    {
        var contentTypeService = context.Get<ContentTypeClient>();

        var contentTypes = await contentTypeService.GetAllAsync();

        context.Set(contentTypes);
    }
}
