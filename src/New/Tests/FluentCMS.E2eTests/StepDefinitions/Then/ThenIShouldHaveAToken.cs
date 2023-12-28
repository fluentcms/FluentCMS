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
    [Then("I Should Have a Token")]
    public void ThenIShouldHaveAToken()
    {
        var authResponse = context.Get<UserLoginResponseIApiResult>();
        authResponse.Data.Token.ShouldNotBeEmpty();
    }
}
