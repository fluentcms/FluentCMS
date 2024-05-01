namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Response Errors Should not be Empty")]
    public void ThenResponseErrorsShouldNotBeEmpty()
    {
        var apiExceptionResult = context.Get<ApiExceptionResult>();
        apiExceptionResult.Errors.ShouldNotBeEmpty();
    }
}
