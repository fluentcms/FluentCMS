using System.Net;
using FluentCMS.Host;
using FluentCMS.Host.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.IntegrationTests;

public sealed class HostTests
{
    [Fact]
    public async Task Web_host_answers_the_health_endpoint_and_runs_the_job_queue()
    {
        using var install = new TemporaryInstall();
        var app = FluentCmsHost.BuildWebApplication([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(install.Configuration);
        });

        await using (app)
        {
            await app.StartAsync();

            Assert.Contains(app.Services.GetServices<IHostedService>(), service => service is JobQueueWorker);

            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());

            await app.StopAsync();
        }
    }

    [Fact]
    public async Task Web_host_serves_the_admin_shell_and_the_showcase_only_in_development()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, ["--environment", "Development"]);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var home = await host.Client.GetAsync("/");
        var homeHtml = await home.Content.ReadAsStringAsync();
        var rtl = await host.Client.GetStringAsync("/?dir=rtl&theme=dark");
        var showcase = await host.Client.GetAsync("/ui-spike");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("<html lang=\"en\" dir=\"ltr\"", homeHtml);
        Assert.Contains("_content/FluentCMS.Admin/css/app.generated", homeHtml);
        Assert.Contains("_framework/blazor.web", homeHtml);
        Assert.Contains("<html lang=\"en\" dir=\"rtl\" class=\"dark\"", rtl);
        Assert.Equal(HttpStatusCode.OK, showcase.StatusCode);
        Assert.Contains("role=\"menu\"", await showcase.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Showcase_is_not_found_outside_development()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var showcase = await host.Client.GetAsync("/ui-spike");

        Assert.Equal(HttpStatusCode.NotFound, showcase.StatusCode);
    }

    [Fact]
    public async Task Mcp_host_has_no_web_server_and_stops_when_standard_input_closes()
    {
        using var install = new TemporaryInstall();
        using var host = FluentCmsHost.BuildMcpHost(
            [],
            standardInput: Stream.Null,
            configure: builder => builder.Configuration.AddInMemoryCollection(install.Configuration));

        Assert.Null(host.Services.GetService<IServer>());
        Assert.Contains(host.Services.GetServices<IHostedService>(), service => service is JobQueueWorker);

        var run = host.RunAsync();
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(run, finished);
        await run;
    }
}
