using System.Net;
using FluentCMS.Admin;
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
    public async Task Web_host_serves_the_admin_shell_with_the_theme_toggle_and_the_reconnect_display()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var home = await host.Client.GetAsync("/");
        var homeHtml = await home.Content.ReadAsStringAsync();
        var rtl = await host.Client.GetStringAsync("/?dir=rtl");

        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("<html lang=\"en\" dir=\"ltr\">", homeHtml);
        Assert.Contains("_content/FluentCMS.Admin/css/app.generated", homeHtml);
        Assert.Contains("_framework/blazor.web", homeHtml);
        Assert.Contains("data-testid=\"theme-toggle\"", homeHtml);
        Assert.Contains("data-theme-choice=\"system\"", homeHtml);
        Assert.Contains("id=\"components-reconnect-modal\"", homeHtml);
        Assert.Contains("<html lang=\"en\" dir=\"rtl\">", rtl);
    }

    [Theory]
    [InlineData("dark", " class=\"dark\"")]
    [InlineData("light", " class=\"light\"")]
    [InlineData("blue", "")]
    [InlineData(null, "")]
    public async Task The_root_element_carries_the_stored_theme_and_otherwise_none_so_the_system_preference_applies(string? stored, string rootClass)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);
        if (stored is not null)
        {
            host.Cookies.Add(host.Client.BaseAddress!, new Cookie(ThemeCookie.Name, stored));
        }

        var home = await host.Client.GetStringAsync("/");
        var signIn = await host.Client.GetStringAsync("/sign-in");

        Assert.Contains($"<html lang=\"en\" dir=\"ltr\"{rootClass}>", home);
        Assert.Contains($"<html lang=\"en\" dir=\"ltr\"{rootClass}>", signIn);
    }

    [Fact]
    public async Task The_showcase_is_for_signed_in_users_in_every_environment_and_replaces_the_spike_page()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var anonymous = await host.Client.GetAsync("/ui");
        await host.SignInAsync(AdminHost.Owner);
        var showcase = await host.Client.GetAsync("/ui");
        var html = await showcase.Content.ReadAsStringAsync();
        var spike = await host.Client.GetAsync("/ui-spike");

        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.Equal("/sign-in?ReturnUrl=%2Fui", host.LocationOf(anonymous));
        Assert.Equal(HttpStatusCode.OK, showcase.StatusCode);
        Assert.Contains("data-slot=\"dialog-content\"", html);
        Assert.Contains("role=\"menu\"", html);
        Assert.Contains("role=\"combobox\"", html);
        Assert.Contains("<kbd", html);
        Assert.Equal(HttpStatusCode.NotFound, spike.StatusCode);
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
