using FluentCMS.E2eTests.ApiClients;
using Shouldly;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("App GetAll result should have {int} items")]
    public void ThenAppGetAllResultShouldHaveItems(int count)
    {
        var pagingResult = context.Get<AppResponseIApiPagingResult>();
        pagingResult.TotalCount.ShouldBe(count);
    }
}
