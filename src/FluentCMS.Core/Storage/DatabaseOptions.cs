using System.ComponentModel.DataAnnotations;
using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

public enum SchemaSyncMode
{
    Off,
    Auto,
}

// FluentCms:Database. A value left out takes its default: MigrateOnStartup the provider's, SchemaSync Auto in
// Development on a provider that supports it.
public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public const string MigrateOnStartupKey = $"{FluentCmsOptions.Section}:{Section}:{nameof(MigrateOnStartup)}";
    public const string SchemaSyncKey = $"{FluentCmsOptions.Section}:{Section}:{nameof(SchemaSync)}";

    public bool? MigrateOnStartup { get; set; }

    [EnumDataType(typeof(SchemaSyncMode))]
    public SchemaSyncMode? SchemaSync { get; set; }

    // From FluentCmsBuilder.AllowSchemaSyncOutsideDevelopment. Internal, so that configuration does not bind it.
    internal bool SchemaSyncOutsideDevelopment { get; init; }
}

internal sealed record DatabaseStartup(bool MigrateOnStartup, SchemaSyncMode SchemaSync)
{
    public static DatabaseStartup Resolve(IDataProvider provider, DatabaseOptions? options, IHostEnvironment? environment, ILogger logger)
    {
        var syncAllowed = provider.SupportsSchemaSync && (environment?.IsDevelopment() == true || options?.SchemaSyncOutsideDevelopment == true);
        var migrateOnStartup = options?.MigrateOnStartup ?? provider.MigrateOnStartup;
        var schemaSync = options?.SchemaSync ?? (syncAllowed ? SchemaSyncMode.Auto : SchemaSyncMode.Off);

        if (schemaSync == SchemaSyncMode.Auto && !syncAllowed)
        {
            MigrationLog.SchemaSyncRefused(logger, DatabaseOptions.SchemaSyncKey);
            schemaSync = SchemaSyncMode.Off;
        }

        return new DatabaseStartup(migrateOnStartup, schemaSync);
    }
}
