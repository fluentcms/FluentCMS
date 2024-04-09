using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have a ContentTypeUpdateRequest")]
    public void GivenIHaveAContentTypeUpdateRequest(Table table)
    {
        var createResult = context.Get<ContentTypeDetailResponseIApiResult>();

        var updateRequest = table.CreateInstance<ContentTypeUpdateRequest>();

        updateRequest.Id = createResult.Data.Id;

        context.Set(updateRequest);
    }
}
