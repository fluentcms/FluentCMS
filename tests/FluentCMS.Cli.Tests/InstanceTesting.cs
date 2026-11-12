using System.Text.Json;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Cli.Tests;

[FluentCMS.Schema.Collection("note")]
public sealed class Note : ContentItem
{
    [Text, Required]
    public string Title { get; set; } = "";
}

// A running instance for the commands that take --url: the test host, which the MCP plugin is discovered in, reached through
// its test server's handler. No queue has workers.
public sealed class InstanceFixture : IAsyncDisposable
{
    public const string Url = "http://localhost/";

    public InstanceFixture()
    {
        Host = new FluentCmsTestHost()
            .WithContentTypes<Note>()
            .WithServices(services => services.AddSingleton<IEmailSender>(new DiscardedEmails()))
            .Configure(cms =>
            {
                cms.PublicOrigin = "https://cms.example.com";
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
    }

    public FluentCmsTestHost Host { get; }

    public HttpMessageHandler Handler => ((TestServer)Host.Services.GetRequiredService<IServer>()).CreateHandler();

    public async Task<string> KeyAsync(string role = BuiltInRoles.SuperAdminName) =>
        (await Host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: TestContext.Current.CancellationToken)).Secret;

    public ValueTask DisposeAsync() => Host.DisposeAsync();

    private sealed class DiscardedEmails : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

// A run of the tool with standard input and the instance's handler, and what it wrote.
internal sealed class InstanceRun(HttpMessageHandler handler)
{
    public LineCountingWriter Output { get; } = new();

    public StringWriter Error { get; } = new();

    public Task<int> RunAsync(params string[] args) => RunAsync(TextReader.Null, args);

    public async Task<int> RunAsync(TextReader input, params string[] args)
    {
        Output.GetStringBuilder().Clear();
        Error.GetStringBuilder().Clear();
        return await CommandLine.RunAsync(args, Output, Error, launcher: null, _ => new HttpClient(handler, disposeHandler: false), input, TestContext.Current.CancellationToken);
    }

    public static List<JsonElement> Items(string json)
    {
        using var document = JsonDocument.Parse(json);
        return [.. document.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone())];
    }
}

// Standard output that another thread can ask how many lines it holds while the tool writes to it.
internal sealed class LineCountingWriter : StringWriter
{
    private readonly Lock _gate = new();
    private int _lines;

    public int Lines => Volatile.Read(ref _lines);

    public override void Write(char value)
    {
        lock (_gate)
        {
            base.Write(value);
        }

        if (value == '\n')
        {
            Interlocked.Increment(ref _lines);
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        lock (_gate)
        {
            base.Write(buffer, index, count);
        }

        Interlocked.Add(ref _lines, buffer.AsSpan(index, count).Count('\n'));
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            base.Write(value);
        }

        Interlocked.Add(ref _lines, value.AsSpan().Count('\n'));
    }

    public override string ToString()
    {
        lock (_gate)
        {
            return base.ToString();
        }
    }
}
