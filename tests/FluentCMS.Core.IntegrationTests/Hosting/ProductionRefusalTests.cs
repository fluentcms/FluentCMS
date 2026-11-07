using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Diagnostics;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// The refusals of an unset public origin and an unencrypted key ring. CmsWebHost lifts both, so each test sets what it is about.
[Xunit.Collection(ProcessEnvironment.Collection)]
public sealed class ProductionRefusalTests : IAsyncDisposable
{
    private const string AllowRequestOrigin = "FluentCms:Security:AllowRequestOrigin";
    private const string AllowUnencryptedKeyRing = "FluentCms:Security:AllowUnencryptedKeyRing";

    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly CmsWebHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task ProductionRefusesAnUnsetPublicOriginBeforeTouchingTheDatabase()
    {
        var app = _host.Build(Environments.Production, new() { [AllowRequestOrigin] = "false" });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1105");
        exception.Message.ShouldContain("FluentCms:PublicOrigin");
        exception.Message.ShouldContain(AllowRequestOrigin);
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Fact]
    public async Task AllowRequestOriginLetsProductionStartAndTheReportKeepsTheDefault()
    {
        var app = _host.Build(Environments.Production, new() { [AllowRequestOrigin] = "true" });

        await app.StartAsync(Token);

        var insecure = Insecure(app);
        insecure.ShouldContain("public-origin-unset");
        insecure.ShouldNotContain("FCMS1105");
    }

    [Fact]
    public async Task APublicOriginLetsProductionStartWithoutTheOptIn()
    {
        var app = _host.Build(Environments.Production, new() { [AllowRequestOrigin] = "false", ["FluentCms:PublicOrigin"] = "https://cms.example.com" });

        await app.StartAsync(Token);

        var insecure = Insecure(app);
        insecure.ShouldNotContain("public-origin-unset");
        insecure.ShouldNotContain("FCMS1105");
    }

    [Fact]
    public async Task ProductionRefusesAnUnencryptedKeyRingBeforeTouchingTheDatabase()
    {
        var app = _host.Build(Environments.Production, new() { [AllowUnencryptedKeyRing] = "false" });

        var exception = await Should.ThrowAsync<FluentCmsConfigurationException>(() => app.StartAsync(Token));

        exception.Code.ShouldBe("FCMS1106");
        exception.Message.ShouldContain(AllowUnencryptedKeyRing);
        exception.Message.ShouldContain("ProtectKeysWithCertificate");
        File.Exists(_host.DatabaseFile).ShouldBeFalse();
    }

    [Fact]
    public async Task AllowUnencryptedKeyRingLetsProductionStartAndTheReportKeepsTheDefault()
    {
        var app = _host.Build(Environments.Production, new() { [AllowUnencryptedKeyRing] = "true" });

        await app.StartAsync(Token);

        var insecure = Insecure(app);
        insecure.ShouldContain("data-protection-keys-unencrypted");
        insecure.ShouldNotContain("FCMS1106");
    }

    [Fact]
    public async Task ACertificateEncryptorLetsProductionStartAndReportsNothing()
    {
        using var certificate = Certificate();
        var app = _host.Build(
            Environments.Production,
            new() { [AllowUnencryptedKeyRing] = "false" },
            builder => builder.Services.AddDataProtection().ProtectKeysWithCertificate(certificate));

        await app.StartAsync(Token);

        app.Services.GetRequiredService<IDataProtectionStatus>().KeysEncrypted.ShouldBeTrue();
        var insecure = Insecure(app);
        insecure.ShouldNotContain("data-protection-keys-unencrypted");
        insecure.ShouldNotContain("FCMS1106");
    }

    [Fact]
    public async Task BothRefusalsAreNamedTogether()
    {
        var app = _host.Build(Environments.Production, new() { [AllowRequestOrigin] = "false", [AllowUnencryptedKeyRing] = "false" });

        var exception = await Should.ThrowAsync<AggregateException>(() => app.StartAsync(Token));

        exception.InnerExceptions.OfType<FluentCmsConfigurationException>().Select(error => error.Code).ShouldBe(["FCMS1105", "FCMS1106"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public async Task OtherEnvironmentsStartAndKeepTheirReportLines(string environment)
    {
        var app = _host.Build(environment, new() { [AllowRequestOrigin] = "false", [AllowUnencryptedKeyRing] = "false" });

        await app.StartAsync(Token);

        var insecure = Insecure(app);
        insecure.ShouldContain("data-protection-keys-unencrypted");
        insecure.ShouldNotContain("public-origin-unset");
        insecure.ShouldNotContain("FCMS1105");
        insecure.ShouldNotContain("FCMS1106");
    }

    private static List<string> Insecure(WebApplication app) =>
        [.. app.Services.GetRequiredService<StartupReportHolder>().Current!.InsecureDefaults.Select(item => item.Id)];

    private static X509Certificate2 Certificate()
    {
        using var rsa = RSA.Create(2048);
        return new CertificateRequest("CN=FluentCMS test key ring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }
}
