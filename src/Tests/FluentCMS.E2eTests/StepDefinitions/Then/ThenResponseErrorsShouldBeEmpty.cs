namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Response Errors Should be Empty")]
    public void ThenResponseErrorsShouldBeEmpty()
    {
        context.ShouldNotContainKey(typeof(ApiClientException).FullName!);
    }

}
