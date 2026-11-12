namespace FluentCMS.Core.Commands;

// The 330x codes are the commands that run inside the host; the fluentcms tool has the 331x codes.
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
}
