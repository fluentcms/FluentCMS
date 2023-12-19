using FluentCMS.Api;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;


namespace FluentCMS.E2eTests;

public abstract class BaseE2eTest<TInterface, TImplementation>
    where TInterface : IApiClient
    where TImplementation : class, TInterface
{
    internal WebApplicationFactory WebUi { get; }

    public HttpClient HttpClient => WebUi.CreateClient();
    public TInterface Client
    {
        get
        {
            using var scope = WebUi.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<TImplementation>();
        }
    }


    public BaseE2eTest()
    {
        // build Our WebUi
        WebUi = new WebApplicationFactory();

    }
}
