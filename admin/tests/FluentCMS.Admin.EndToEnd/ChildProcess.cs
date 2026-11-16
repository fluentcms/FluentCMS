using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace FluentCMS.Admin.EndToEnd;

// A process the tests start, which never outlives its bound or the test run: its whole tree is killed on timeout, on
// cancellation, on dispose and when the test process exits. Its output is kept in memory for the failure message only.
internal sealed class ChildProcess : IDisposable
{
    private const int KeptLines = 400;

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource _outputClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _errorClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ChildProcess(ProcessStartInfo start)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.RedirectStandardInput = true;
        start.UseShellExecute = false;
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += (_, line) => Receive(line.Data, _outputClosed, keep: true);
        _process.ErrorDataReceived += (_, line) => Receive(line.Data, _errorClosed, keep: false);
    }

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    // Standard output, which a command writes its result to; the logs go to standard error.
    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public string Tail => string.Join(Environment.NewLine, _lines);

    public static ChildProcess Start(string fileName, IEnumerable<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string?> environment)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // A dotnet started from inside dotnet test would otherwise inherit the variables of the outer build.
        foreach (var name in start.Environment.Keys.Where(name => name.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(name);
        }

        foreach (var (name, value) in environment)
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        var child = new ChildProcess(start);
        child._process.Start();
        child._process.BeginOutputReadLine();
        child._process.BeginErrorReadLine();
        AppDomain.CurrentDomain.ProcessExit += child.OnTestProcessExit;
        return child;
    }

    // Runs to its end and returns its exit code; past the timeout, or on cancellation, it is killed and this throws.
    public static async Task<(int ExitCode, string Output, string Tail)> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        string? input,
        CancellationToken cancellationToken)
    {
        using var child = Start(fileName, arguments, workingDirectory, environment);
        if (input is not null)
        {
            await child._process.StandardInput.WriteLineAsync(input.AsMemory(), cancellationToken);
        }

        child._process.StandardInput.Close();
        await child.WaitForExitAsync(timeout, cancellationToken);
        return (child.ExitCode, child.Output, child.Tail);
    }

    public async Task WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bound.CancelAfter(timeout);
        try
        {
            await _process.WaitForExitAsync(bound.Token);
            await Task.WhenAll(_outputClosed.Task, _errorClosed.Task).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill();
            throw new TimeoutException($"{Describe()} did not end within {timeout}.{Environment.NewLine}{Tail}");
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw;
        }
    }

    public string Describe() => $"'{Path.GetFileName(_process.StartInfo.FileName)} {string.Join(' ', _process.StartInfo.ArgumentList.Select(Path.GetFileName))}'";

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(TimeSpan.FromSeconds(30));
            }
        }
        catch (InvalidOperationException)
        {
            // Not started, or already gone.
        }
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= OnTestProcessExit;
        Kill();
        _process.Dispose();
    }

    private void OnTestProcessExit(object? sender, EventArgs e) => Kill();

    private void Receive(string? line, TaskCompletionSource closed, bool keep)
    {
        if (line is null)
        {
            closed.TrySetResult();
            return;
        }

        if (keep)
        {
            lock (_output)
            {
                _output.AppendLine(line);
            }
        }

        _lines.Enqueue(line);
        while (_lines.Count > KeptLines && _lines.TryDequeue(out _))
        {
        }
    }
}
