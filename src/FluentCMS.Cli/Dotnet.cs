using System.Diagnostics;

namespace FluentCMS.Cli;

internal static class Dotnet
{
    // Runs the dotnet CLI and returns its standard output, or fails with the code and both outputs.
    public static string Run(string code, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new CliException(CliErrorCodes.HostNotStarted, "dotnet could not be started.");
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? output
            : throw new CliException(code, $"dotnet {arguments[0]} failed:{Environment.NewLine}{output}{errors.GetAwaiter().GetResult()}");
    }
}
