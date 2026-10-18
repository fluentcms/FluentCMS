using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentCMS.Core.Agents;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class AgentAuthenticationTests
{
    private const string Initialize =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}""";

    [Fact]
    public async Task An_agent_with_a_key_connects_over_http_and_every_call_runs_as_that_agent()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, configure: builder => builder.Services.AddSingleton(McpAgents.WhoAmI));
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());

        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        Assert.Contains("whoami", await McpAgents.ToolNamesAsync(client));
        Assert.Equal($"agent {issued.Credential.Id} pipeline-agent", await McpAgents.WhoAmIAsync(client));
    }

    [Fact]
    public async Task A_request_without_an_accepted_key_is_refused_before_it_reaches_the_server()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());
        var key = issued.Key;

        (string Case, HttpRequestMessage Request, bool Refused)[] cases =
        [
            ("no header", Post(), false),
            ("an empty bearer", Post(authorization: "Bearer"), true),
            ("an unknown key", Post(authorization: $"Bearer {AgentKeys.Generate()}"), true),
            ("a malformed key", Post(authorization: "Bearer not-a-key"), true),
            ("another scheme", Post(authorization: $"Basic {key}"), false),
            ("the key in the query string", Post(query: $"?access_token={key}"), false),
            ("the key as a query parameter", Post(query: $"?key={key}"), false),
        ];

        foreach (var (name, request, refused) in cases)
        {
            var response = await host.Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{name}: {response.StatusCode}");
            var challenge = Assert.Single(response.Headers.WwwAuthenticate);
            Assert.Equal("Bearer", challenge.Scheme);
            Assert.Equal(refused, challenge.Parameter?.Contains("error=\"invalid_token\"", StringComparison.Ordinal) == true);
            Assert.Contains(refused ? "not a valid agent key" : "Authorization: Bearer", body, StringComparison.Ordinal);
            Assert.DoesNotContain(key[AgentKeys.Prefix.Length..], body, StringComparison.Ordinal);
        }

        // A signed-in person's admin cookie is not read at /mcp, so it cannot make a call without a key.
        await host.SignInAsync(AdminHost.Owner);
        Assert.True(host.IsSignedIn);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.SendAsync(Post())).StatusCode);
    }

    [Fact]
    public async Task A_key_that_expires_or_is_revoked_is_refused_from_its_next_request()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, configure: builder => builder.Services.AddSingleton<TimeProvider>(clock));
        var ownerId = await host.CreateFirstAdministratorAsync();
        var expiring = await McpAgents.IssueAsync(host, ownerId, McpAgents.Credential(clock.Now) with { ExpiresAt = clock.Now.AddDays(1) });
        var revoked = await McpAgents.IssueAsync(host, ownerId, McpAgents.Credential(clock.Now, "revoked-agent"));

        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post(authorization: $"Bearer {expiring.Key}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.SendAsync(Post(authorization: $"Bearer {revoked.Key}"))).StatusCode);

        await host.AsUserAsync<IAgentCredentialDirectory, Core.Results.Result<AgentCredential>>(
            ownerId,
            directory => directory.RevokeAsync(revoked.Credential.Id));
        clock.Now = clock.Now.AddDays(1);

        foreach (var (key, description, message) in new[]
        {
            (expiring.Key, "The key has expired", "expired at"),
            (revoked.Key, "The key has been revoked", "has been revoked"),
        })
        {
            var response = await host.Client.SendAsync(Post(authorization: $"Bearer {key}"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains($"error_description=\"{description}", Assert.Single(response.Headers.WwwAuthenticate).Parameter);
            Assert.Contains(message, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task No_log_line_contains_a_key()
    {
        using var install = new TemporaryInstall();
        // Everything down to trace, except the request lines ASP.NET Core logs below Warning, as the shipped settings have it:
        // those record whole URLs, so a key a client wrongly puts in one would land there.
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
            ["Logging:LogLevel:Microsoft.AspNetCore.Authentication"] = "Trace",
        });
        var logs = new CapturedLogs();
        host.App.Services.GetRequiredService<ILoggerFactory>().AddProvider(logs);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());

        await using (await McpAgents.ConnectOverHttpAsync(host, issued.Key))
        {
        }

        await host.Client.SendAsync(Post(authorization: $"Bearer {issued.Key}A"));
        await host.Client.SendAsync(Post(query: $"?access_token={issued.Key}"));

        // The calls were logged down to the refusal, so the absence below is not for want of logging.
        Assert.Contains(logs.Lines, line => line.Contains($"{AgentKeyAuthentication.Scheme} was not authenticated", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, line => line.Contains(issued.Key[AgentKeys.Prefix.Length..], StringComparison.Ordinal));
    }

    [Fact]
    public async Task Over_stdio_every_message_runs_as_the_agent_whose_key_the_process_was_given()
    {
        using var install = new TemporaryInstall();
        var issued = await McpAgents.IssueAsync(install.Configuration);
        await using var stdio = await InProcessStdio.StartAsync(install.Configuration, issued.Key);

        await using var client = await stdio.ConnectAsync();

        Assert.Equal($"agent {issued.Credential.Id} pipeline-agent", await McpAgents.WhoAmIAsync(client));
    }

    [Theory]
    [InlineData("", "FLUENTCMS_AGENT_KEY")]
    [InlineData("not-a-key", "not a valid agent key")]
    public async Task Over_stdio_without_an_accepted_key_every_request_is_refused_with_what_to_do(string key, string message)
    {
        using var install = new TemporaryInstall();
        await McpAgents.IssueAsync(install.Configuration);
        await using var stdio = await InProcessStdio.StartAsync(install.Configuration, key);

        var refused = await Assert.ThrowsAnyAsync<McpException>(stdio.ConnectAsync);

        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_key_revoked_during_a_stdio_session_is_refused_from_its_next_message(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        var issued = await McpAgents.IssueAsync(database.Configuration);
        await using var stdio = StdioHost.Start(new Dictionary<string, string?>(database.Configuration)
        {
            ["DataProtection:KeysDirectory"] = install.KeysDirectory,
            [StdioAgentAuthentication.KeyVariable] = issued.Key,
        });

        await using var client = await stdio.ConnectAsync();
        Assert.Equal(McpAgents.BuiltInTools, await McpAgents.ToolNamesAsync(client));

        await McpAgents.RevokeAsync(database.Configuration, issued.Credential.Id);
        var refused = await Assert.ThrowsAnyAsync<McpException>(() => client.ListToolsAsync().AsTask());

        Assert.Contains("has been revoked", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(stdio.ErrorLines, line => line.Contains(issued.Key[AgentKeys.Prefix.Length..], StringComparison.Ordinal));
    }

    private static HttpRequestMessage Post(string? authorization = null, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, McpEndpoints.Route + query)
        {
            Content = new StringContent(Initialize, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return request;
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyCollection<string> Lines => _lines;

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturedLogs owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                owner._lines.Enqueue(formatter(state, exception) + exception);
            }
        }
    }
}
