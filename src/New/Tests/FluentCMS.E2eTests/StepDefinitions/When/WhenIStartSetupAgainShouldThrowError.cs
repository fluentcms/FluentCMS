using FluentCMS.E2eTests.ApiClients;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Start Setup again should throw Error")]
    public async Task WhenIStartSetupAgainShouldThrowError()
    {
        var setupClient = context.Get<SetupClient>();
        var e = await (setupClient.StartAsync().ShouldThrowAsync<ApiClientException>());
        // TODO: fetch and check error message
    }
}
