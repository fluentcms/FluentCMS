namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresTestRole", Order = 40)]
    public async Task RequiresTestRole()
    {
        var table = new Table("field", "value");
        table.AddRow("name", "DummyRole");
        table.AddRow("description", "DummyRole Description");

        GivenIHaveAService("RoleClient");
        GivenIHaveARole(table);
        await WhenICreateRoleAsync();
        ThenRoleCreationResultShouldBeSuccess();
}
}
