namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresAuthenticatedAdmin", Order = 25)]
    public async Task RequiresAuthenticatedAdmin()
    {

        var table = new Table("field", "value");
        table.AddRow("username", "superadmin");
        table.AddRow("password", "Passw0rd!");

        GivenIHaveAService("AccountClient");
        GivenIHaveCredentials(table);
        await WhenIAuthenticateAsync();
        ThenResponseErrorsShouldBeEmpty();
        ThenIShouldHaveAToken();
    }
}
