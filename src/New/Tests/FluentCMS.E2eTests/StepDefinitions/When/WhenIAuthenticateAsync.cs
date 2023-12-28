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
    [When("I Authenticate")]
    public async Task WhenIAuthenticateAsync()
    {
        var accountClient = context.Get<AccountClient>();
        var credentials = context.Get<Credential>();
        var authBody = new UserLoginRequest()
        {
            Username = credentials.Username,
            Password = credentials.Password
        };
        try
        {
            var response = await accountClient.AuthenticateAsync(authBody);
            context.Set(response);
            context.Set(response.Errors);
        }
        catch (ApiClientException e)
        {
            // todo: fetch error messages
            
        }
    }
}
