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
    private const string _isInitializedKey = "IsInitialized";

    [Then("Setup initialization status should be {word}")]
    public void ThenSetupInitializationStatusShouldBe(string statusStr)
    {
        var status = bool.Parse(statusStr);
        var isInitialized = context.Get<BooleanIApiResult>(_isInitializedKey);
        isInitialized.Data.ShouldBe(status);
    }
}
