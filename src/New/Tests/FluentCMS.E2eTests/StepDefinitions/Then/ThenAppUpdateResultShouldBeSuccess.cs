using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App Update result should be Success")]
    public void ThenAppUpdateResultShouldBeSuccess()
    {
        var appResponse = context.Get<AppResponseIApiResult>();
        appResponse.Errors.ShouldBeEmpty();
        appResponse.Data.ShouldNotBeNull();
        var appId = context.Get<Guid>("appId");
        appResponse.Data.Id.ShouldBe(appId);
    }
}
