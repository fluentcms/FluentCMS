namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Given("I have a ContentType")]
    public void GivenIHaveAContentType()
    {
        var createdContentType = context.Get<ContentTypeResponseIApiResult>();

        createdContentType.ShouldNotBeNull();
    }
}
