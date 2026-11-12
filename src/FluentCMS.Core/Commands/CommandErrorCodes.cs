namespace FluentCMS.Core.Commands;

// The 330x codes are the commands that run inside the host; the fluentcms tool has the 331x codes. The schema tools and
// doctor have 3320 to 3334, those of the host here and those of the tool in CliErrorCodes.
internal static class CommandErrorCodes
{
    public const string BadUsage = "FCMS3300";
    public const string NoEntryPoint = "FCMS3301";
    public const string CommandFailed = "FCMS3302";
    public const string MigrationsPending = "FCMS3303";
    public const string UnknownStream = "FCMS3304";
    public const string UnknownMigration = "FCMS3305";
    public const string ScriptNotSupported = "FCMS3306";
    public const string OutputNotWritten = "FCMS3307";
    public const string SchemaInvalid = "FCMS3320";
    public const string SchemaFileUnreadable = "FCMS3321";
    public const string SchemaDrift = "FCMS3322";
    public const string NoAppliedSchema = "FCMS3323";
    public const string AppliedSchemaUnreadable = "FCMS3324";
    public const string HostWouldNotStart = "FCMS3325";
    public const string MixedVersions = "FCMS3326";
}
