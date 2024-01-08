namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should see {int} content types")]
    public void ThenIShouldSeeContentTypes(int count)
    {
        var contentTypes = context.Get<ContentTypeResponseIApiPagingResult>();

        contentTypes.TotalCount.ShouldBe(count);
    }
}
