using Shouldly;
using System.Collections;
using Error = FluentCMS.E2eTests.ApiClients.Error;

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
