using TechTalk.SpecFlow.Assist;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Given("I have a role")]
    public void GivenIHaveARole(Table table)
    {
        var role = table.CreateInstance<RoleCreateRequest>();
        context.Set(role);
    }
}
