using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{

    [Then("user is created")]
    public void ThenUserIsCreated()
    {
        var userCreateResponse = context.Get<UserResponseIApiResult>();
        userCreateResponse.Errors.ShouldBeEmpty();
        userCreateResponse.Data.ShouldNotBeNull();
        userCreateResponse.Data.Id.ShouldNotBe(default);

    }
}
