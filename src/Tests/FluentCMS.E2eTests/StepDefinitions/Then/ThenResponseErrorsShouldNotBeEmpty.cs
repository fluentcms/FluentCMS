namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Response Errors Should not be Empty")]
    public void ThenResponseErrorsShouldNotBeEmpty()
    {

        var errors = context.Get<List<AppError>>();
        errors.ShouldNotBeEmpty();
    }
}
