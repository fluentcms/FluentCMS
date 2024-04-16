
using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [When("I set a field")]
    public async Task WhenISetAFieldAsync(Table table)
    {

        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var contentTypeClient = context.Get<ContentTypeClient>();

        var body = table.CreateInstance<ContentTypeFieldSetRequest>();
        body.FillMetadata(table);

        var result = await contentTypeClient.SetFieldAsync(
            contentType.Data.Id,
            body);

        context.Set(body);
        context.Set(result);
    }
}
