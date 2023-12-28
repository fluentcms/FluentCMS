using FluentCMS.E2eTests.ApiClients;
using FluentCMS.E2eTests.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.E2eTests.StepDefinitions;
public partial class StepDefinitions
{
    [When("I Register")]
    public async Task WhenIRegister()
    {
        var accountClient = context.Get<AccountClient>();
        var credentials = context.Get<Credential>();
        var registerBody = new UserRegisterRequest()
        {
            Username = credentials.Username,
            Password = credentials.Password,
            Email = credentials.Email
        };
        var response = await accountClient.RegisterAsync(registerBody);
        context.Set(response);
        context.Set(response.Errors);
    }
}
