using System.Reflection;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using FluentCMS.Core.Plugins;
using FluentCMS.Core.Storage;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Core.Schema;
using FluentCMS.Diagnostics;
using Microsoft.AspNetCore.HostFiltering;
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
    PublicOrigin origin,
    PluginRegistry plugins,
    ProviderSlotRegistry slots,
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
            Plugins = Plugins(),
            ProviderSlots = slots.Describe(services),
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
            InsecureDefaults = InsecureDefaults(startup),
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
        Plugins = Plugins(),
        ProviderSlots = slots.Describe(services),
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
    private List<InsecureDefault> InsecureDefaults(DatabaseStartup startup)
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

        if (environment?.IsProduction() == true && AllowsAnyHost())
        {
            var links = origin.Base is null ? ", and links in password reset and invitation emails go to the host it names" : "";
            found.Add(new InsecureDefault { Id = "allowed-hosts-any", Message = $"AllowedHosts is '*' or not set in Production, so the instance answers a request with any Host header{links}. Set AllowedHosts to the host names of the instance." });
        }

        if (environment?.IsProduction() == true && origin.Base is null)
        {
            found.Add(new InsecureDefault { Id = "public-origin-unset", Message = $"{FluentCmsOptions.Section}:{PublicOrigin.Key} is not set in Production, so links in password reset and invitation emails and the issuer of the authorization server follow the request's scheme, host and path base. Set it to the address users reach the instance at, such as https://cms.example.com." });
        }

        if (startup.MigrateOnStartup && startup.SchemaSync == SchemaSyncMode.Auto && environment?.IsDevelopment() != true)
        {
            found.Add(new InsecureDefault { Id = "schema-sync-outside-development", Message = "Content tables follow the schema at start outside Development: additive changes apply with no reviewed migration, and destructive ones are logged and left undone. The host allowed it with AllowSchemaSyncOutsideDevelopment." });
        }

        if (proxy.ForwardedHeaders is { Enabled: true } forwarded && forwarded.TrustsEveryAddress)
        {
            found.Add(new InsecureDefault { Id = "forwarded-headers-trust-any", Message = "Forwarded headers are trusted from every address, so any caller can choose the scheme and client address the host sees. List the proxies or networks." });
        }

        found.AddRange(services.GetServices<IInsecureDefaultSource>()
            .SelectMany(source => source.GetInsecureDefaults())
            .Select(item => item with { Message = SecretScrubber.Of(item.Message) }));

        return found;
    }

    // Web host defaults read AllowedHosts into these options and make an unset value "*"; without them nothing filters hosts.
    private bool AllowsAnyHost() =>
        services.GetService<IOptions<HostFilteringOptions>>()?.Value.AllowedHosts is not { Count: > 0 } hosts || hosts.Contains("*");

    private List<StartupPlugin> Plugins() =>
        [.. plugins.Plugins.Select(plugin => new StartupPlugin { Name = plugin.Name, Version = plugin.Version })];

    // The assemblies of FluentCMS in the process, which is the SDK, core, the data provider and whatever else the host
    // referenced. A plugin's assembly shows here, and the plugin in Plugins.
    private static List<StartupAssembly> Assemblies() =>
        [.. AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && assembly.GetName().Name is { } name && name.StartsWith("FluentCMS", StringComparison.Ordinal))
            .Select(assembly => new StartupAssembly { Name = assembly.GetName().Name!, Version = VersionOf(assembly) })
            .DistinctBy(assembly => assembly.Name)
            .OrderBy(assembly => assembly.Name, StringComparer.Ordinal)];

    internal static string VersionOf(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
