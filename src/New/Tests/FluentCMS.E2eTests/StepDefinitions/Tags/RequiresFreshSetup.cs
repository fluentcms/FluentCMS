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
        await WhenIStartSetup();
        await ThenWaitSeconds(2);
        await WhenIFetchSetupIsInitialized();
        ThenSetupInitializationStatusShouldBe("True");

    }
}
