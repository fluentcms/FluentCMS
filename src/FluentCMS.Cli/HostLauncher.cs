using System.ComponentModel;
using System.Diagnostics;

namespace FluentCMS.Cli;

// Starts the host with "fluentcms <arguments>" and returns its exit code. Tests replace it.
internal interface IHostLauncher
{
    Task<int> RunAsync(HostTarget host, string? environment, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken);
}

// dotnet exec <assembly> fluentcms <arguments>, in the project directory and with the caller's environment, so the
// command runs with the host's own FluentCMS.Core, its configuration for the environment and its plugins.
internal sealed class ProcessHostLauncher : IHostLauncher
{
    public const string LogToStandardError = "Logging__Console__LogToStandardErrorThreshold";

    public async Task<int> RunAsync(HostTarget host, string? environment, IReadOnlyList<string> arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        // On the console the host writes to it itself, which keeps its colours, its encoding and a prompt; other writers,
        // such as a test's, get what it writes.
        var inherit = ReferenceEquals(output, Console.Out) && ReferenceEquals(error, Console.Error);
        var start = new ProcessStartInfo(Dotnet.Path)
        {
            WorkingDirectory = host.ProjectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = !inherit,
            RedirectStandardError = !inherit,
        };

        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(host.AssemblyPath);
        start.ArgumentList.Add("fluentcms");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            start.Environment["DOTNET_ENVIRONMENT"] = environment;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        }

        // The console logger writes to standard error, so that standard output is the command's own and can be redirected.
        if (!start.Environment.ContainsKey(LogToStandardError))
        {
            start.Environment[LogToStandardError] = "Trace";
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new CliException(CliErrorCodes.HostNotStarted, $"{host.AssemblyPath} could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new CliException(CliErrorCodes.HostNotStarted, $"dotnet could not be started to run {host.AssemblyPath}: {exception.Message}");
        }

        using (process)
        {
            // Ctrl+C reaches the host as well, which stops on its own; the tool waits for it rather than leaving it behind.
            ConsoleCancelEventHandler wait = (_, args) => args.Cancel = true;
            if (inherit)
            {
                Console.CancelKeyPress += wait;
            }

            try
            {
                using var stop = cancellationToken.Register(() => Kill(process));
                var pumps = inherit ? [] : new[] { PumpAsync(process.StandardOutput, output), PumpAsync(process.StandardError, error) };
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(pumps);
                cancellationToken.ThrowIfCancellationRequested();
                return process.ExitCode;
            }
            finally
            {
                if (inherit)
                {
                    Console.CancelKeyPress -= wait;
                }
            }
        }
    }

    private static async Task PumpAsync(StreamReader reader, TextWriter writer)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            await writer.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It has exited.
        }
    }
}
