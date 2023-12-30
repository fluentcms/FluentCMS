using FluentCMS.E2eTests.ApiClients;
using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have App Update Request")]
    public void GivenIHaveAppUpdateRequest(Table table)
    {
        var appUpdateRequest = table.CreateInstance<AppUpdateRequest>();
        context.Set(appUpdateRequest);
    }

}
