namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should see the updated content type")]
    public void ThenIShouldSeeTheUpdatedContentType()
    {
        var updateRequest = context.Get<ContentTypeUpdateRequest>();
        var updateResponse = context.Get<ContentTypeDetailResponseIApiResult>();

        updateResponse.Data.Title.ShouldBe(updateRequest.Title);
        updateResponse.Data.Description.ShouldBe(updateRequest.Description);
    }
}
