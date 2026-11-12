namespace FluentCMS.Cli;

// The 331x codes; the commands inside the host have the 330x codes.
internal static class CliErrorCodes
{
    public const string ProjectNotBuilt = "FCMS3310";
    public const string AssemblyNotFound = "FCMS3311";
    public const string HostNotStarted = "FCMS3312";
    public const string NoContentMigration = "FCMS3313";
    public const string MigrationApplied = "FCMS3314";
    public const string AppliedMigrationsUnknown = "FCMS3315";
    public const string InvalidInstance = "FCMS3316";
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
