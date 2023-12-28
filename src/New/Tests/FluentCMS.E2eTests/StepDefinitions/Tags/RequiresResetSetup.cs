using FluentCMS.E2eTests.ApiClients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Before("RequiresResetSetup", Order = 10)]
    public async Task RequiresResetSetup()
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

    }

}
