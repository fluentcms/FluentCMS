using System.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Host.Tests;

// The image's entry point is "dotnet FluentCMS.Host.dll", so "docker run <image> fluentcms migrate" runs this: the host,
// in a process of its own, with nothing set but the database, which ends with the command's exit code.
public sealed class HostCommandTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-host-commands-");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try
        {
            _directory.Delete(recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task TheHostRunsMigrateAsAnInitContainerDoesAndExitsWithTheCommandsCode()
    {
        var (exitCode, output, error) = await RunAsync("fluentcms", "migrate", "--check");
        exitCode.ShouldBe(1, error);
        output.ShouldContain($"system{Environment.NewLine}  ");
        error.ShouldContain("FCMS3303: ");

        (exitCode, output, error) = await RunAsync("fluentcms", "migrate");
        exitCode.ShouldBe(0, error);
        output.ShouldContain($"media{Environment.NewLine}  ");
        output.ShouldContain("Applied ");

        (exitCode, output, error) = await RunAsync("fluentcms", "migrate", "--check");
        exitCode.ShouldBe(0, error);
        output.ShouldBe($"No migration is pending.{Environment.NewLine}");
    }

    [Fact]
    public async Task BadUsageExitsWithTwo()
    {
        var (exitCode, _, error) = await RunAsync("fluentcms", "migrate", "--now");

        exitCode.ShouldBe(2);
        error.ShouldContain("FCMS3300: --now is not an option of 'fluentcms migrate'.");
        File.Exists(Path.Combine(_directory.FullName, "cms.db")).ShouldBeFalse();
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _directory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[] { "exec", typeof(ReferenceHost).Assembly.Location }.Concat(args))
        {
            start.ArgumentList.Add(argument);
        }

        // As the image sets it, with the database in the test's directory, and the logs on standard error, as the tool asks.
        start.Environment["ConnectionStrings__FluentCms"] = $"Data Source={Path.Combine(_directory.FullName, "cms.db")};Pooling=False";
        start.Environment["Logging__Console__LogToStandardErrorThreshold"] = "Trace";
        start.Environment.Remove("ASPNETCORE_ENVIRONMENT");
        start.Environment.Remove("DOTNET_ENVIRONMENT");

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(Token);
        var output = await process.StandardOutput.ReadToEndAsync(Token);
        await process.WaitForExitAsync(Token);
        return (process.ExitCode, output, await error);
    }
}
