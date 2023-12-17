using FluentCMS.Api;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Headers;
using System.Reflection;
using static System.Net.WebRequestMethods;


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
        WebUi = new WebApplicationFactory<Program>();

        // setup Client
        Client = (TInterface)Activator.CreateInstance(typeof(TImplementation), [HttpClient])!;

        using var scope = WebUi.Services.CreateScope();

        // reset DB
        scope.ServiceProvider.ResetMongoDb();
    }
}
