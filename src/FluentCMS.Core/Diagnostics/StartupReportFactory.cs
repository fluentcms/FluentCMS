using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Core.Schema;
using FluentCMS.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.Diagnostics;

internal sealed class StartupReportFactory(
    IServiceProvider services,
    IDataProvider provider,
    MigrationRunner migrations,
    SchemaModel model,
    IOptions<DatabaseOptions> database,
    ReverseProxyOptions proxy,
    IEnumerable<IProductionStartupCheck> checks,
    TimeProvider time,
    IHostEnvironment? environment = null)
{
    public async Task<StartupReport> CreateAsync(CancellationToken cancellationToken)
    {
        var startup = DatabaseStartup.Resolve(provider, database.Value, environment, NullLogger.Instance);
        var pending = await migrations.GetPendingMigrationsAsync(cancellationToken);
        var schema = await SchemaAsync(cancellationToken);

        return new StartupReport
        {
            StartedAt = time.GetUtcNow(),
            Environment = EnvironmentName,
            Version = VersionOf(typeof(StartupReportFactory).Assembly),
            DataProvider = new StartupDataProvider
            {
                Provider = provider.Name,
                ProviderVersion = VersionOf(provider.GetType().Assembly),
                MigrateOnStartup = startup.MigrateOnStartup,
                SchemaSync = startup.SchemaSync.ToString(),
            },
            Assemblies = Assemblies(),
            PendingMigrations = [.. pending.Select(migration => new StartupMigration { Stream = migration.Stream, Id = SecretScrubber.Of(migration.Id) })],
            Schema = new StartupSchema
            {
                Hash = schema.Hash,
                AppliedHash = schema.AppliedHash,
                AppliedAt = schema.AppliedAt,
                AppliedVersion = schema.AppliedVersion,
                Drifted = schema.Drifted,
                Changes = schema.Changes is null ? null : SecretScrubber.Of(schema.Changes),
            },
            InsecureDefaults = InsecureDefaults(),
        };
    }

    // What is left when the report cannot be made: the facts that need no database, and the type of what failed.
    public StartupReport CreateDegraded(Exception exception) => new()
    {
        StartedAt = time.GetUtcNow(),
        Environment = EnvironmentName,
        Version = VersionOf(typeof(StartupReportFactory).Assembly),
        Failure = exception.GetType().Name,
        DataProvider = new StartupDataProvider { Provider = provider.Name, ProviderVersion = VersionOf(provider.GetType().Assembly) },
        Assemblies = Assemblies(),
    };

    private string EnvironmentName => environment?.EnvironmentName ?? "Unknown";

    // Like the drift check at startup, a history that cannot be read does not stop the host, and the report says so.
    private async Task<SchemaStatus> SchemaAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await migrations.GetSchemaStatusAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SchemaStatus(SchemaSnapshot.Of(model).Hash, null, null, null, Drifted: false, $"The schema history could not be read ({exception.GetType().Name}).");
        }
    }

    // The checks that refuse a start in Production are the first source, so that the report and the refusal never disagree.
    private List<InsecureDefault> InsecureDefaults()
    {
        List<InsecureDefault> found = [.. checks
            .Select(check => check.Check())
            .OfType<StartupRefusal>()
            .Select(refusal => new InsecureDefault { Id = refusal.Code, Message = SecretScrubber.Of(refusal.Message) })];

        if (services.GetService<ISigningKeyStatus>()?.IsGenerated == true && found.All(item => item.Id != HostErrorCodes.GeneratedSigningKeyInProduction))
        {
            found.Add(new InsecureDefault { Id = "generated-signing-key", Message = $"Tokens are signed with a key the server generated; {GeneratedSigningKeyCheck.Key} accepts it. Configure a signing certificate." });
        }

        if (services.GetService<IDataProtectionStatus>()?.KeysEncrypted == false)
        {
            found.Add(new InsecureDefault { Id = "data-protection-keys-unencrypted", Message = "The Data Protection key ring is stored unencrypted, so a reader of the database can forge sign-in cookies. Add an XML encryptor such as ProtectKeysWithCertificate." });
        }

        if (services.GetService<IEmailSender>() is LogEmailSender)
        {
            found.Add(new InsecureDefault { Id = "development-email-sender", Message = "Email is written to the log and not sent, links included. Register an IEmailSender." });
        }

        if (environment?.IsProduction() == true && !proxy.HttpsRedirection.Enabled)
        {
            found.Add(new InsecureDefault { Id = "https-redirection-off", Message = $"HTTPS redirection is off in Production. Turn on {FluentCmsOptions.Section}:{ReverseProxyOptions.Section}:{nameof(ReverseProxyOptions.HttpsRedirection)}:Enabled unless the proxy in front redirects." });
        }

        if (proxy.ForwardedHeaders is { Enabled: true } forwarded && forwarded.TrustsEveryAddress)
        {
            found.Add(new InsecureDefault { Id = "forwarded-headers-trust-any", Message = "Forwarded headers are trusted from every address, so any caller can choose the scheme and client address the host sees. List the proxies or networks." });
        }

        return found;
    }

    // The assemblies of FluentCMS in the process, which is the SDK, core, the data provider and whatever else the host
    // referenced. A plugin shows here and, once the plugin host exists, in Plugins.
    private static List<StartupAssembly> Assemblies() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.GetName().Name is { } name && name.StartsWith("FluentCMS", StringComparison.Ordinal))
            .Select(assembly => new StartupAssembly { Name = assembly.GetName().Name!, Version = VersionOf(assembly) })
            .DistinctBy(assembly => assembly.Name)
            .OrderBy(assembly => assembly.Name, StringComparer.Ordinal)];

    private static string VersionOf(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
