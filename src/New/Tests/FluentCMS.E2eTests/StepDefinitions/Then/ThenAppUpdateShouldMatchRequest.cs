using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App Update should match request")]
    public void ThenAppUpdateShouldMatchRequest()
    {
        var appResponse = context.Get<AppResponseIApiResult>();
        var appUpdateRequest = context.Get<AppUpdateRequest>();
        appResponse.Data.Slug.ShouldBe(appUpdateRequest.Slug);
        appResponse.Data.Title.ShouldBe(appUpdateRequest.Title);
        appResponse.Data.Description.ShouldBe(appUpdateRequest.Description);
    }
}
