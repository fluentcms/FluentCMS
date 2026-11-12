namespace FluentCMS.Core.Commands;

// A command that cannot do what it was asked, with its code and the exit code of the process.
internal sealed class CommandException(string code, string message, int exitCode = CommandExitCodes.Failure)
    : Exception($"{code}: {message}")
{
    public string Code { get; } = code;

    public int ExitCode { get; } = exitCode;
}

internal static class CommandExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int BadUsage = 2;
}
