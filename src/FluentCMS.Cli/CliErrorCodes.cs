namespace FluentCMS.Cli;

// The 331x codes, and those of 3327 to 3334 that the schema tools and doctor have in the tool; the commands inside the host
// have the 330x codes and the rest of 3320 to 3334. Of the commands that administer an instance, the 336x codes are the
// tool's, and the 335x codes are the host's and the tool's alike, since an instance reached with --url answers the same way.
// Export and import at an instance have 3251 to 3253, and share 3248 with the commands inside the host. The generate
// commands have 3370 to 3378.
internal static class CliErrorCodes
{
    public const string UnknownRole = "FCMS3350";
    public const string UnknownSite = "FCMS3351";
    public const string UserNotFound = "FCMS3353";
    public const string UnexpectedAnswer = "FCMS3361";
    public const string McpKeyRefused = "FCMS3362";
    public const string KeyWithoutTls = "FCMS3363";

    public const string ProjectNotBuilt = "FCMS3310";
    public const string AssemblyNotFound = "FCMS3311";
    public const string HostNotStarted = "FCMS3312";
    public const string NoContentMigration = "FCMS3313";
    public const string MigrationApplied = "FCMS3314";
    public const string AppliedMigrationsUnknown = "FCMS3315";
    public const string InvalidInstance = "FCMS3316";
    public const string DestructiveMigration = "FCMS3327";
    public const string MigrationUnchecked = "FCMS3328";
    public const string InstanceUnreachable = "FCMS3329";
    public const string InstanceRefused = "FCMS3330";
    public const string HostSnapshotFailed = "FCMS3331";
    public const string TransferFileNotRead = "FCMS3248";
    public const string TransferRefused = "FCMS3251";
    public const string TransferUnreachable = "FCMS3252";
    public const string TransferAnswerUnexpected = "FCMS3253";

    // The generate commands; FCMS3379 is the host's.
    public const string SchemaUnreadable = "FCMS3370";
    public const string InstanceRequestFailed = "FCMS3371";
    public const string OutputStale = "FCMS3372";
    public const string OutputNotWritten = "FCMS3373";
    public const string InvalidNamespace = "FCMS3374";
    public const string NoGeneratedType = "FCMS3375";
    public const string NameNotGenerated = "FCMS3376";
    public const string ConflictingSources = "FCMS3377";
    public const string NoHostInput = "FCMS3378";
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int BadUsage = 2;
}

// A failure the tool reports with its code and no stack trace.
internal sealed class CliException(string code, string message, int exitCode = ExitCodes.Failure)
    : InvalidOperationException($"{code}: {message}")
{
    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;
}
