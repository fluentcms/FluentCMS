namespace FluentCMS.Core.Storage;

internal static class StorageErrorCodes
{
    public const string MissingContentSchema = "FCMS1301";
    public const string NoDataProvider = "FCMS1302";
    public const string SeveralDataProviders = "FCMS1303";
    public const string InvalidDatabaseSetting = "FCMS1304";
    public const string PendingStreamMigrations = "FCMS1305";

    // Numbered with the reference host's codes, which brought it.
    public const string SchemaSyncNotSupported = "FCMS3154";
}
