using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Response Errors Should be Empty")]
    public void ThenResponseErrorsShouldBeEmpty()
    {
        var errors = context.Get<ICollection<Error>>();
        errors.ShouldBeEmpty();
    }

}
