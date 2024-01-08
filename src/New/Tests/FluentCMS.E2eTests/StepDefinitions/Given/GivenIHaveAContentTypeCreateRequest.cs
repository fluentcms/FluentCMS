using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have a ContentTypeCreateRequest")]
    public void GivenIHaveAContentTypeCreateRequest(Table table)
    {
        var createRequest = table.CreateInstance<ContentTypeCreateRequest>();

        context.Set(createRequest);
    }
}
