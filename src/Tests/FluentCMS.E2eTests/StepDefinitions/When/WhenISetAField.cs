
namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I set a field")]
    public async Task WhenISetAFieldAsync()
    {

        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var contentTypeClient = context.Get<ContentTypeClient>();

        //var body = new ContentTypeField()
        //{
        //    Title = "Test Field",
        //    DefaultValue = "Test Field Default Value",
        //    Description = "Test Field Description",
        //    Hint = "Test Field Hint",
        //    IsRequired = true,
        //    Label = "Test Label",
        //    Placeholder = "Test Placeholder",
        //    Slug = "test-field",
        //};

        //var result = await contentTypeClient.SetFieldAsync(
        //    contentType.Data.Id,
        //    body);

        //context.Set(result);
    }
}
