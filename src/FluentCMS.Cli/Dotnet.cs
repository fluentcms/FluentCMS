using System.ComponentModel;
using System.Diagnostics;

namespace FluentCMS.Cli;

internal static class Dotnet
{
    // The muxer the SDK runs under names itself in DOTNET_HOST_PATH; otherwise dotnet from PATH.
    public static string Path => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    // Runs the dotnet CLI and returns its standard output, or fails with the code and both outputs.
    public static string Run(string code, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new CliException(CliErrorCodes.HostNotStarted, "dotnet could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new CliException(CliErrorCodes.HostNotStarted, $"dotnet could not be started: {exception.Message}");
        }

        using (process)
        {
            var errors = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0
                ? output
                : throw new CliException(code, $"dotnet {arguments[0]} failed:{Environment.NewLine}{output}{errors.GetAwaiter().GetResult()}");
        }
    }
}
