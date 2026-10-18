using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace FluentCMS.IntegrationTests.Mcp;

// fluentcms mcp in a process of its own, started the way a desktop agent starts it, with its standard error kept apart.
internal sealed class StdioHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _errorLines = new();

    private StdioHost(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is not null)
            {
                _errorLines.Enqueue(line.Data);
            }
        };
    }

    public IReadOnlyCollection<string> ErrorLines => _errorLines;

    public static StdioHost Start(IReadOnlyDictionary<string, string?> settings)
    {
        var startInfo = new ProcessStartInfo(DotnetMuxer())
        {
            ArgumentList = { Path.Combine(AppContext.BaseDirectory, "fluentcms.dll"), "mcp" },
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Settings reach the process as environment variables, as an agent's MCP configuration passes them.
        foreach (var (key, value) in settings)
        {
            startInfo.Environment[key.Replace(":", "__")] = value;
        }

        var host = new StdioHost(Process.Start(startInfo)!);
        host._process.BeginErrorReadLine();
        return host;
    }

    public Task<McpClient> ConnectAsync()
    {
        return McpClient.CreateAsync(new StreamClientTransport(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream));
    }

    // Closing standard input is how an MCP client ends the session.
    public async Task<int> CloseInputAndWaitForExitAsync()
    {
        _process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _process.WaitForExitAsync(timeout.Token);
        return _process.ExitCode;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    // The muxer of the runtime the tests run on, which lives at <root>/shared/Microsoft.NETCore.App/<version>/.
    private static string DotnetMuxer()
    {
        var root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
    }
}
