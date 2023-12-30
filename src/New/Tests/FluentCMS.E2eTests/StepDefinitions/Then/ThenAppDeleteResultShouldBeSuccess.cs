using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App Delete result should be Success")]
    public void ThenAppDeleteResultShouldBeSuccess()
    {
        var deleteApiResult = context.Get<BooleanIApiResult>();
        deleteApiResult.Errors.ShouldBeEmpty();
        deleteApiResult.Data.ShouldBeTrue();
    }
}
