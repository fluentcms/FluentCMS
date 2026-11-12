namespace FluentCMS.Testing;

// What a command wrote and its exit code: 0, 1 for a failure, 2 for bad usage.
public sealed record TestCommandResult(int ExitCode, string Output, string Error);
