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
        using var database = new TemporarySqliteDatabase();
        var app = FluentCmsHost.BuildWebApplication([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(database.Configuration);
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
    public async Task Mcp_host_has_no_web_server_and_stops_when_standard_input_closes()
    {
        using var database = new TemporarySqliteDatabase();
        using var host = FluentCmsHost.BuildMcpHost(
            [],
            standardInput: Stream.Null,
            configure: builder => builder.Configuration.AddInMemoryCollection(database.Configuration));

        Assert.Null(host.Services.GetService<IServer>());
        Assert.Contains(host.Services.GetServices<IHostedService>(), service => service is JobQueueWorker);

        var run = host.RunAsync();
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(run, finished);
        await run;
    }
}
