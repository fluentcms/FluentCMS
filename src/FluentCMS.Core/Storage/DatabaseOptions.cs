using FluentCMS.Core.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Core.Storage;

internal enum SchemaSyncMode
{
    Off,
    Auto,
}

// Database:MigrateOnStartup and Database:SchemaSync, with their defaults.
internal sealed record DatabaseOptions(bool MigrateOnStartup, SchemaSyncMode SchemaSync)
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";
    public const string SchemaSyncKey = "Database:SchemaSync";

    public static DatabaseOptions Resolve(IDataProvider provider, IConfiguration? configuration, IHostEnvironment? environment, ILogger logger)
    {
        var syncAllowed = provider.SupportsSchemaSync && environment?.IsDevelopment() == true;
        var migrateOnStartup = ReadBoolean(configuration?[MigrateOnStartupKey]) ?? provider.MigrateOnStartup;
        var schemaSync = ReadSchemaSync(configuration?[SchemaSyncKey]) ?? (syncAllowed ? SchemaSyncMode.Auto : SchemaSyncMode.Off);

        if (schemaSync == SchemaSyncMode.Auto && !syncAllowed)
        {
            MigrationLog.SchemaSyncRefused(logger, SchemaSyncKey);
            schemaSync = SchemaSyncMode.Off;
        }

        return new DatabaseOptions(migrateOnStartup, schemaSync);
    }

    private static bool? ReadBoolean(string? value) => value switch
    {
        null => null,
        _ when bool.TryParse(value, out var parsed) => parsed,
        _ => throw Invalid(MigrateOnStartupKey, value, "true or false"),
    };

    private static SchemaSyncMode? ReadSchemaSync(string? value) => value switch
    {
        null => null,
        _ when value.Equals(nameof(SchemaSyncMode.Auto), StringComparison.OrdinalIgnoreCase) => SchemaSyncMode.Auto,
        _ when value.Equals(nameof(SchemaSyncMode.Off), StringComparison.OrdinalIgnoreCase) => SchemaSyncMode.Off,
        _ => throw Invalid(SchemaSyncKey, value, "Auto or Off"),
    };

    private static FluentCmsConfigurationException Invalid(string key, string value, string expected) =>
        new(StorageErrorCodes.InvalidDatabaseSetting, $"{key} is '{value}'; it must be {expected}.");
}
