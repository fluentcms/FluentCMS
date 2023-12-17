

using FluentCMS.Api;
using FluentCMS.Repositories.MongoDB;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;


namespace FluentCMS.E2eTests;

public abstract class BaseE2eTest
{
    public WebApplicationFactory<Program> WebUi { get; }
    public HttpClient HttpClient => WebUi.CreateClient();

    public BaseE2eTest()
    {
        // build Our WebUi
        WebUi = new WebApplicationFactory<Program>();

        // reset DB
        using var scope = WebUi.Services.CreateScope();
        scope.ServiceProvider.ResetMongoDb();
    }
}
