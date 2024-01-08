using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have a ContentTypeUpdateRequest")]
    public void GivenIHaveAContentTypeUpdateRequest(Table table)
    {
        // fetch app and created content type from previous steps
        var app  = context.Get<AppResponseIApiResult>();
        var createResult = context.Get<ContentTypeResponseIApiResult>();

        var updateRequest = table.CreateInstance<ContentTypeUpdateRequest>();

        updateRequest.AppId = app.Data.Id;
        updateRequest.Id = createResult.Data.Id;

        context.Set(updateRequest);
    }
}
