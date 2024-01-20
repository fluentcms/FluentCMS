namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("I should get {int} Roles")]
    public void ThenIShouldGetRoles(int count)
    {
        var roles = context.Get<RoleDetailResponseIApiPagingResult>();
        roles.TotalCount.ShouldBe(count);
    }
}
