using FluentCMS.E2eTests.ApiClients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Start Setup")]
    public async Task WhenIStartSetup()
    {
        await context.Get<SetupClient>().StartAsync();
    }
}
