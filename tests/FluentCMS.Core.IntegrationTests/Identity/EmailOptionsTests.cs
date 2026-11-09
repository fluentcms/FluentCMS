using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Identity;
using FluentCMS.Core.IntegrationTests.Hosting;
using FluentCMS.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

public sealed class EmailOptionsTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task AnSmtpHostReplacesTheLogSenderEvenInDevelopment()
    {
        var app = _host.Build(Environments.Development, new Dictionary<string, string?>
        {
            ["FluentCms:Email:Smtp:Host"] = "smtp.example.com",
            ["FluentCms:Email:Smtp:From"] = "cms@example.com",
        });
        await app.StartAsync(Token);

        app.Services.GetRequiredService<IEmailSender>().ShouldBeOfType<SmtpEmailSender>();
        app.Services.GetRequiredService<EmailOptions>().Smtp.Port.ShouldBe(587);
        app.Services.GetRequiredService<EmailOptions>().Smtp.Security.ShouldBe(SmtpSecurity.StartTls);
        app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id).ShouldNotContain("development-email-sender");
    }

    [Fact]
    public async Task WithoutAnSmtpHostProductionRefusesToSend()
    {
        var app = _host.Build(Environments.Production);
        await app.StartAsync(Token);

        var sender = app.Services.GetRequiredService<IEmailSender>();

        sender.ShouldBeOfType<UnconfiguredEmailSender>();
        (await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync(new EmailMessage("ada@example.com", "Hi", "Text"), Token))).Message.ShouldStartWith("FCMS3008");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not an address")]
    public async Task AnSmtpHostWithoutAValidSenderStopsTheHost(string? from)
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?>
        {
            ["FluentCms:Email:Smtp:Host"] = "smtp.example.com",
            ["FluentCms:Email:Smtp:From"] = from,
        });

        var error = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        error.Code.ShouldBe("FCMS1101");
        error.Message.ShouldContain("FluentCms:Email:Smtp:From");
    }

    [Fact]
    public async Task AnInvalidPortStopsTheHost()
    {
        var app = _host.Build(Environments.Production, new Dictionary<string, string?>
        {
            ["FluentCms:Email:Smtp:Host"] = "smtp.example.com",
            ["FluentCms:Email:Smtp:From"] = "cms@example.com",
            ["FluentCms:Email:Smtp:Port"] = "0",
        });

        var error = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        error.Code.ShouldBe("FCMS1101");
        error.Message.ShouldContain("FluentCms:Email:Smtp");
    }
}
