using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App GetAll result should be Success")]
    public void ThenAppGetAllResultShouldBeSuccess()
    {
        var pagingResult = context.Get<AppResponseIApiPagingResult>();
        pagingResult.Errors.ShouldBeEmpty();
        pagingResult.Data.ShouldNotBeNull();
        pagingResult.Data.ShouldNotBeEmpty();
    }
}
