using FluentCMS.E2eTests.ApiClients;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresFreshSetup", Order = 20)]
    public async Task RequiresFreshSetup()
    {
        GivenIHaveAService("SetupClient");
        await WhenIFetchSetupIsInitialized();
        if (context.Get<BooleanIApiResult>(_isInitializedKey).Data)
        {
            await WhenIResetSetup();
            await ThenWaitSeconds(2);
            await WhenIFetchSetupIsInitialized();
        }
        ThenSetupInitializationStatusShouldBe("False");
        var table = new Table("field","value");
        table.AddRow("username", "superadmin");
        table.AddRow("password", "Passw0rd!");
        table.AddRow("email", "superadmin@localhost");
        await WhenIStartSetup(table);
        await ThenWaitSeconds(2);
        await WhenIFetchSetupIsInitialized();
        ThenSetupInitializationStatusShouldBe("True");

    }
}
