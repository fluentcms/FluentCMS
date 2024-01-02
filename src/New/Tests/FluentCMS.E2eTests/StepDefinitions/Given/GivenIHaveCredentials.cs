using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have Credentials")]
    public void GivenIHaveCredentials(Table table)
    {
        var credentials = table.CreateInstance<UserRegisterRequest>();
        context.Set(credentials);
    }
}
