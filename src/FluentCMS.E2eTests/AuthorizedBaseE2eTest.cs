using FluentCMS.Web.UI.ApiClients;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.E2eTests;

public class AuthorizedBaseE2eTest<TInterface, TImplementation> : BaseE2eTest<TInterface, TImplementation>
        where TInterface : IApiClient
        where TImplementation : class, TInterface
{
    protected async Task Login(string userName = "superadmin", string password = "Passw0rd!")
    {
        var scope = WebUi.Services.CreateScope();
        var accountClient = scope.ServiceProvider.GetRequiredService<AccountClient>();

        var resp = await accountClient.LoginAsync(new UserLoginRequest() { Username = userName, Password = password });

        E2eHttpClientFactory<Program>.OverrideToken = resp.Data.Token;
    }

}
