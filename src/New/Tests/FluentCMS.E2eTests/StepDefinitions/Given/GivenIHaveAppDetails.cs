using FluentCMS.E2eTests.ApiClients;
using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have App Create Request")]
    public void GivenIHaveAppCreateRequest(Table table)
    {
        var appDetails = table.CreateInstance<AppCreateRequest>();
        context.Set(appDetails);
    }
}
