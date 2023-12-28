using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [Then("Wait {int} second(s)")]
    public async Task ThenWaitSeconds(int seconds)
    {
        await Task.Delay(TimeSpan.FromSeconds(seconds));
    }
}
