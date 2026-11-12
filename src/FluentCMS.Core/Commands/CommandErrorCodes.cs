namespace FluentCMS.Core.Commands;

// The 330x codes are the commands that run inside the host; the fluentcms tool has the 331x codes. The schema tools and
// doctor have 3320 to 3334, those of the host here and those of the tool in CliErrorCodes, and schema apply 3335 to 3349. Of
// the commands that administer an instance (B-91), the 335x codes are shared by the host and the tool and the 336x codes are
// the tool's.
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
    public const string UnknownRole = "FCMS3350";
    public const string UnknownSite = "FCMS3351";
    public const string PasswordNotRead = "FCMS3352";
    public const string UserNotFound = "FCMS3353";
    public const string PublicOriginRequired = "FCMS3354";
    public const string UnknownJob = "FCMS3355";
    public const string InlineJobFailed = "FCMS3356";
    public const string UnknownSeeder = "FCMS3357";
    public const string SeedingClaimed = "FCMS3358";
    public const string InvalidValue = "FCMS3359";
    public const string ContentMigrationsExist = "FCMS3335";
    public const string StreamsNotMigrated = "FCMS3336";
    public const string DestructiveSchemaChange = "FCMS3337";
    public const string SchemaChangeNotApplicable = "FCMS3338";
    public const string SchemaApplyFailed = "FCMS3339";
    public const string SchemaNotRecorded = "FCMS3340";
}
