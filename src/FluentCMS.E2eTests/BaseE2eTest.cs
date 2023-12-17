using FluentCMS.Api;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;


namespace FluentCMS.E2eTests;

public abstract class BaseE2eTest<TInterface, TImplementation>
    where TInterface : IApiClient
    where TImplementation : class, TInterface
{
    public WebApplicationFactory<Program> WebUi { get; }
    public HttpClient HttpClient => WebUi.CreateClient();
    public required TInterface Client { get; init; }


    public BaseE2eTest()
    {
        // build Our WebUi
        WebUi = new WebApplicationFactory();

        // setup Client
        //Client = (TInterface)Activator.CreateInstance(typeof(TImplementation), [HttpClient])!;

        using var scope = WebUi.Services.CreateScope();

        Client = scope.ServiceProvider.GetRequiredService<TImplementation>();
        // reset DB
        scope.ServiceProvider.ResetMongoDb();
    }
}
