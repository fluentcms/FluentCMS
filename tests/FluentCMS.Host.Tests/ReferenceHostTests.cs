using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Providers;
using FluentCMS.Schema;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

public sealed class ReferenceHostTests : IAsyncDisposable
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    private readonly HostUnderTest _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task WithNothingSetProductionRefusesBeforeTouchingTheDatabaseAndNamesWhatToSet()
    {
        var app = _host.Build(optIns: false);

        var exception = await Should.ThrowAsync<AggregateException>(() => app.StartAsync(Token));

        var refusals = exception.InnerExceptions.Cast<FluentCmsConfigurationException>().ToDictionary(refusal => refusal.Code, refusal => refusal.Message);
        refusals.Keys.ShouldBe(["FCMS1102", "FCMS1105", "FCMS1106"], ignoreOrder: true);
        refusals["FCMS1102"].ShouldContain("FluentCms:Security:AllowGeneratedSigningKey");
        refusals["FCMS1105"].ShouldContain("FluentCms:PublicOrigin");
        refusals["FCMS1105"].ShouldContain("FluentCms:Security:AllowRequestOrigin");
        refusals["FCMS1106"].ShouldContain("FluentCms:Security:AllowUnencryptedKeyRing");
        File.Exists(Path.Combine(_host.Root, "fluentcms.db")).ShouldBeFalse();
    }

    [Fact]
    public async Task WithTheEvaluationOptInsItStartsOnSqliteInTheContentRootAndIsReady()
    {
        await _host.StartAsync(Operator.Settings());

        using var client = _host.Client();
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);
        File.Exists(Path.Combine(_host.Root, "fluentcms.db")).ShouldBeTrue();

        var report = await Operator.ReportAsync(_host);
        report["environment"]!.GetValue<string>().ShouldBe("Production");
        report["dataProvider"]!["provider"]!.GetValue<string>().ShouldBe("SQLite");
        var insecure = Operator.InsecureDefaults(report);
        insecure.ShouldContain("allowed-hosts-any");
        insecure.ShouldContain("public-origin-unset");
        insecure.ShouldContain("generated-signing-key");
        insecure.ShouldContain("data-protection-keys-unencrypted");
        insecure.ShouldNotContain("development-email-sender");
        insecure.ShouldNotContain("schema-sync-outside-development");
        insecure.ShouldNotContain("webhooks-private-networks");
    }

    [Fact]
    public async Task TheHostRunsTheWebhooksPluginAndReportsPrivateNetworksInProduction()
    {
        var settings = Operator.Settings();
        settings["FluentCms:Webhooks:AllowPrivateNetworks"] = "true";
        await _host.StartAsync(settings);

        using var api = await Operator.SignInAsync(_host);
        using var webhooks = await api.GetAsync(new Uri("/api/management/v1/webhooks", UriKind.Relative), Token);
        var report = await Operator.ReportAsync(_host);

        webhooks.StatusCode.ShouldBe(HttpStatusCode.OK);
        report["plugins"]!.AsArray().Select(plugin => plugin!["name"]!.GetValue<string>()).ShouldContain("webhooks");
        Operator.InsecureDefaults(report).ShouldContain("webhooks-private-networks");
    }

    [Fact]
    public async Task TheBlogSampleServesItsSeededContentThroughTheDeliveryApi()
    {
        var settings = Operator.Settings();
        settings[ReferenceHost.SampleVariable] = "blog";
        await _host.StartAsync(settings);

        using var api = await Operator.SignInAsync(_host);
        using var delivery = await Operator.DeliveryClientAsync(_host, api, "article", "author", "tag");

        using var welcome = await delivery.GetAsync(new Uri("/api/delivery/v1/article/by/slug/welcome-to-fluentcms", UriKind.Relative), Token);
        var (status, body) = await HostUnderTest.ReadAsync(welcome);
        status.ShouldBe(HttpStatusCode.OK, body?.ToJsonString());
        var english = body!["title"]!.GetValue<string>();
        english.ShouldNotBeNullOrWhiteSpace();

        using var french = await delivery.GetAsync(new Uri($"/api/delivery/v1/article/{body["id"]}?locale=fr&fallback=none", UriKind.Relative), Token);
        var (frenchStatus, frenchBody) = await HostUnderTest.ReadAsync(french);
        frenchStatus.ShouldBe(HttpStatusCode.OK, frenchBody?.ToJsonString());
        frenchBody!["slug"]!.GetValue<string>().ShouldBe("bienvenue-dans-fluentcms");
        frenchBody["title"]!.GetValue<string>().ShouldNotBe(english);

        using var tags = await delivery.GetAsync(new Uri("/api/delivery/v1/tag", UriKind.Relative), Token);
        var (tagStatus, tagBody) = await HostUnderTest.ReadAsync(tags);
        tagStatus.ShouldBe(HttpStatusCode.OK, tagBody?.ToJsonString());
        tagBody!["items"]!.AsArray().Count.ShouldBe(3);
    }

    // The example of docs/docker.md.
    [Fact]
    public async Task TheSchemaFileDefinesTheContentTypes()
    {
        var file = _host.WriteFile("schema.json", """
            {
              "collections": [
                {
                  "name": "post",
                  "localized": true,
                  "fields": [
                    { "name": "title", "type": "text", "required": true, "maxLength": 200, "localizable": true },
                    { "name": "body", "type": "richText", "localizable": true },
                    { "name": "publishedOn", "type": "dateTime", "indexed": true }
                  ]
                }
              ],
              "singletons": [
                { "name": "site-settings", "fields": [ { "name": "tagline", "type": "text" } ] }
              ]
            }
            """);

        var settings = Operator.Settings();
        settings[ReferenceHost.SchemaVariable] = file;
        var app = await _host.StartAsync(settings);

        var schema = app.Services.GetRequiredService<SchemaModel>();
        schema.FindContentType("post").ShouldNotBeNull().Kind.ShouldBe(SchemaTypeKind.Collection);
        schema.FindContentType("site-settings").ShouldNotBeNull().Kind.ShouldBe(SchemaTypeKind.Singleton);
        Operator.InsecureDefaults(await Operator.ReportAsync(_host)).ShouldContain("schema-sync-outside-development");
    }

    [Theory]
    [InlineData("{ \"collections\": [ ", "FCMS1213")]
    [InlineData("{ \"pages\": [] }", "FCMS1213")]
    [InlineData("{ \"collections\": [ { \"name\": \"page\", \"fields\": [ { \"name\": \"hero\", \"type\": \"media\" } ] } ] }", "FCMS1203")]
    [InlineData("{ \"collections\": [ { \"name\": \"page\", \"fields\": [ { \"name\": \"author\", \"type\": \"relation\", \"target\": \"person\" } ] } ] }", "FCMS1204")]
    public void AnInvalidSchemaFileStopsTheHostWithItsCode(string json, string code)
    {
        var file = _host.WriteFile("schema.json", json);

        var error = Should.Throw<SchemaValidationException>(() => _host.Build(new() { [ReferenceHost.SchemaVariable] = file }));

        error.Errors.Select(item => item.Code).ShouldContain(code);
        error.Message.ShouldContain(code);
    }

    [Theory]
    [InlineData("PostgreSql", "Host=db;Database=cms;Username=cms;Password=secret")]
    [InlineData("SqlServer", "Server=db;Database=cms;User Id=cms;Password=secret;TrustServerCertificate=true")]
    public void ASchemaFileOnAServerProviderIsRefusedBecauseItsTablesComeFromMigrations(string provider, string connectionString)
    {
        var file = _host.WriteFile("schema.json", """{ "collections": [ { "name": "post", "fields": [ { "name": "title", "type": "text" } ] } ] }""");

        var error = Should.Throw<FluentCmsConfigurationException>(() => _host.Build(new()
        {
            [ReferenceHost.SchemaVariable] = file,
            [ReferenceHost.ProviderKey] = provider,
            [$"ConnectionStrings:{ReferenceHost.ConnectionStringName}"] = connectionString,
        }));

        error.Code.ShouldBe("FCMS3154");
    }

    [Fact]
    public void AMissingSchemaFileStopsTheHost()
    {
        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new() { [ReferenceHost.SchemaVariable] = Path.Combine(_host.Root, "absent.json") }));

        error.Code.ShouldBe("FCMS3153");
        error.Message.ShouldContain("absent.json");
    }

    [Theory]
    [InlineData(" ", false)]
    [InlineData(" Blog ", true)]
    public void ABlankSampleIsUnsetAndTheNameIgnoresSpacesAndCase(string sample, bool loaded)
    {
        var app = _host.Build(new() { [ReferenceHost.SampleVariable] = sample });

        (app.Services.GetRequiredService<SchemaModel>().FindContentType("article") is not null).ShouldBe(loaded);
    }

    [Fact]
    public void AnUnknownSampleStopsTheHost()
    {
        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new() { [ReferenceHost.SampleVariable] = "shop" }));

        error.Code.ShouldBe("FCMS3152");
    }

    [Fact]
    public void AnUnknownDataProviderStopsTheHost()
    {
        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new() { [ReferenceHost.ProviderKey] = "Oracle" }));

        error.Code.ShouldBe("FCMS3150");
    }

    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void AServerProviderNeedsAConnectionString(string provider)
    {
        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new() { [ReferenceHost.ProviderKey] = provider }));

        error.Code.ShouldBe("FCMS3151");
        error.Message.ShouldContain("ConnectionStrings:FluentCms");
    }

    [Theory]
    [InlineData("postgresql", "Host=db;Database=cms;Username=cms;Password=secret", "PostgreSQL")]
    [InlineData("SqlServer", "Server=db;Database=cms;User Id=cms;Password=secret;TrustServerCertificate=true", "SQL Server")]
    [InlineData("SQLITE", "Data Source=elsewhere.db", "SQLite")]
    public void TheProviderIsChosenByConfiguration(string provider, string connectionString, string name)
    {
        var app = _host.Build(new() { [ReferenceHost.ProviderKey] = provider, [$"ConnectionStrings:{ReferenceHost.ConnectionStringName}"] = connectionString });

        app.Services.GetRequiredService<FluentCMS.Core.Storage.IDataProvider>().Name.ShouldBe(name);
    }

    [Fact]
    public async Task TheImageDefaultsServeNoOpenApiDocumentOrScalarUi()
    {
        await _host.StartAsync();

        using var client = _host.Client();
        (await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/scalar", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync(new Uri("/scalar/", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OpenApiEnabledServesTheDocumentOfTheLoadedTypes()
    {
        var file = _host.WriteFile("schema.json", """{ "collections": [ { "name": "post", "fields": [ { "name": "title", "type": "text" } ] } ] }""");
        await _host.StartAsync(new() { [ReferenceHost.SchemaVariable] = file, ["FluentCms:OpenApi:Enabled"] = "true" });

        using var client = _host.Client();
        using var document = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), Token);
        var (status, body) = await HostUnderTest.ReadAsync(document);
        status.ShouldBe(HttpStatusCode.OK);
        body!["paths"]!.AsObject().Select(path => path.Key).ShouldContain("/api/delivery/v1/post");
        (await client.GetAsync(new Uri("/scalar/", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllowedHostsTurnsAwayAForgedHostHeader()
    {
        await _host.StartAsync(new() { ["AllowedHosts"] = "cms.example.com" });

        using var allowed = _host.Client(new Uri("https://cms.example.com/"));
        using var forged = _host.Client(new Uri("https://evil.example/"));

        (await allowed.GetAsync(new Uri("/health/live", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await forged.GetAsync(new Uri("/health/live", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await forged.GetAsync(new Uri("/account/forgot-password", UriKind.Relative), Token)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task APublicOriginNamesTheIssuerWhateverHostTheRequestNames()
    {
        var settings = Operator.Settings();
        settings["FluentCms:PublicOrigin"] = "https://cms.example.com";
        await _host.StartAsync(settings);

        using var forged = _host.Client(new Uri("https://evil.example/"));
        using var discovery = await forged.GetAsync(new Uri("/.well-known/openid-configuration", UriKind.Relative), Token);
        var (status, body) = await HostUnderTest.ReadAsync(discovery);

        status.ShouldBe(HttpStatusCode.OK);
        body!["issuer"]!.GetValue<string>().ShouldBe("https://cms.example.com/");
        Operator.InsecureDefaults(await Operator.ReportAsync(_host)).ShouldNotContain("public-origin-unset");
    }

    [Fact]
    public async Task AKeyRingCertificateEncryptsTheRingSoProductionNeedsNoOptInForIt()
    {
        var settings = Operator.Settings();
        settings[ReferenceHost.KeyRingCertificatePathKey] = WriteCertificate("ring.pfx", "ring-password");
        settings[ReferenceHost.KeyRingCertificatePasswordKey] = "ring-password";
        settings["FluentCms:Security:AllowUnencryptedKeyRing"] = "false";
        var app = await _host.StartAsync(settings);

        // The token is signed with the generated key, which is kept protected with a key of the ring.
        var report = await Operator.ReportAsync(_host);

        app.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor.ShouldBeOfType<CertificateXmlEncryptor>();
        app.Services.GetServices<IHostedService>().Select(service => service.GetType().Name).ShouldNotContain("DataProtectionHostedService");
        Operator.InsecureDefaults(report).ShouldNotContain("data-protection-keys-unencrypted");
        var ring = RingXml();
        ring.ShouldNotBeEmpty();
        ring.ShouldAllBe(xml => xml.Contains("encryptedSecret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("missing.pfx", null)]
    [InlineData("ring.pfx", "wrong-password")]
    public void AKeyRingCertificateThatCannotBeReadStopsTheHost(string file, string? password)
    {
        WriteCertificate("ring.pfx", "ring-password");

        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new()
        {
            [ReferenceHost.KeyRingCertificatePathKey] = Path.Combine(_host.Root, file),
            [ReferenceHost.KeyRingCertificatePasswordKey] = password,
        }));

        error.Code.ShouldBe("FCMS3155");
        error.Message.ShouldContain(ReferenceHost.KeyRingCertificatePathKey);
    }

    [Fact]
    public void AKeyRingCertificateWithoutAnRsaKeyStopsTheHost()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = new CertificateRequest("CN=FluentCMS test key ring", ecdsa, HashAlgorithmName.SHA256)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(_host.Root, "ring.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, "ring-password"));

        var error = Should.Throw<ReferenceHostException>(() => _host.Build(new()
        {
            [ReferenceHost.KeyRingCertificatePathKey] = path,
            [ReferenceHost.KeyRingCertificatePasswordKey] = "ring-password",
        }));

        error.Code.ShouldBe("FCMS3155");
    }

    [Fact]
    public async Task EmailGoesThroughTheConfiguredSmtpServerWithItsCredentials()
    {
        await using var smtp = new FakeSmtpServer();
        var app = await _host.StartAsync(new()
        {
            ["FluentCms:Email:Smtp:Host"] = "127.0.0.1",
            ["FluentCms:Email:Smtp:Port"] = smtp.Port.ToString(CultureInfo.InvariantCulture),
            ["FluentCms:Email:Smtp:Security"] = "None",
            ["FluentCms:Email:Smtp:UserName"] = "mailer",
            ["FluentCms:Email:Smtp:Password"] = "mail-password",
            ["FluentCms:Email:Smtp:From"] = "FluentCMS <cms@example.com>",
        }, environment: "Development");

        await app.Services.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage("ada@example.com", "Hello", "Plain words.") { HtmlBody = "<p>Rich words.</p>" }, Token);

        smtp.Logins.ShouldBe(["mailer:mail-password"]);
        using var message = FakeSmtpServer.Parse(smtp.Messages.ShouldHaveSingleItem());
        message.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("cms@example.com");
        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("ada@example.com");
        message.Subject.ShouldBe("Hello");
        message.TextBody.ShouldNotBeNull().Trim().ShouldBe("Plain words.");
        message.HtmlBody.ShouldNotBeNull().Trim().ShouldBe("<p>Rich words.</p>");
    }

    private string WriteCertificate(string name, string password)
    {
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=FluentCMS test key ring", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var path = Path.Combine(_host.Root, name);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
        return path;
    }

    private List<string> RingXml()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_host.Root, "fluentcms.db")};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT xml FROM cms_data_protection_key";
        using var reader = command.ExecuteReader();
        List<string> rows = [];
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
