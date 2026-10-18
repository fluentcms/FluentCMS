using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Persistence;
using FluentCMS.Infrastructure.Agents;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class ToolCallRecordingTests
{
    private static readonly McpServerTool Fails = McpServerTool.Create(
        () => new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "Refused." }] },
        new McpServerToolCreateOptions { Name = "fails" });

    private static readonly McpServerTool Throws = McpServerTool.Create(
        new Func<string>(() => throw new InvalidOperationException("Broken.")),
        new McpServerToolCreateOptions { Name = "throws" });

    // Writes inside a unit of work and throws before committing, so its change rolls back.
    private static readonly McpServerTool RollsBack = McpServerTool.Create(
        async (IServiceProvider services) =>
        {
            await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
            await services.GetRequiredService<IAuditLog>().AppendAsync(new NewAuditEvent("test.rolled_back"));
            throw new InvalidOperationException("Rolled back.");
        },
        new McpServerToolCreateOptions { Name = "rolls-back" });

    private static readonly McpServerTool ProtocolError = McpServerTool.Create(
        new Func<string>(() => throw new McpProtocolException("Not allowed here.", McpErrorCode.InvalidParams)),
        new McpServerToolCreateOptions { Name = "protocol-error" });

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_call_over_http_is_recorded_with_its_agent_and_what_it_declared(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration, configure: AddTestTools);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        await client.CallToolAsync("whoami", options: new RequestOptions
        {
            Meta = new JsonObject { [ToolCallRecording.ModelKey] = "example-model-1", [ToolCallRecording.TaskKey] = "TASK-42" },
        });
        await client.CallToolAsync("whoami");
        await client.CallToolAsync("whoami", options: new RequestOptions
        {
            Meta = new JsonObject { [ToolCallRecording.ModelKey] = new string('m', 200), [ToolCallRecording.TaskKey] = 42 },
        });

        var calls = await CallsAsync(host);
        Assert.Equal(3, calls.Count);
        Assert.All(calls, call =>
        {
            Assert.Equal(issued.Credential.Id, call.AgentCredentialId);
            Assert.Equal("pipeline-agent", call.AgentName);
            Assert.Equal("whoami", call.Tool);
            Assert.Equal(ToolCallOutcome.Succeeded, call.Outcome);
            Assert.True(call.Duration >= TimeSpan.Zero);
        });
        Assert.Equal(["example-model-1", null, new string('m', ToolCall.MaxDeclaredLength)], calls.Select(call => call.Model));
        Assert.Equal(["TASK-42", null, null], calls.Select(call => call.TaskId));
    }

    // A tool that throws is recorded as errored; the SDK then answers with an error result, or a protocol error for one.
    [Fact]
    public async Task Each_outcome_is_recorded_and_the_caller_gets_the_same_answer_as_without_the_log()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, configure: AddTestTools);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        var failed = await client.CallToolAsync("fails");
        var thrown = await client.CallToolAsync("throws");
        var protocol = await Assert.ThrowsAsync<McpProtocolException>(() => client.CallToolAsync("protocol-error").AsTask());

        Assert.True(failed.IsError);
        Assert.Equal("Refused.", Assert.IsType<TextContentBlock>(Assert.Single(failed.Content)).Text);
        Assert.True(thrown.IsError);
        Assert.Contains("Not allowed here.", protocol.Message, StringComparison.Ordinal);

        var calls = await CallsAsync(host);
        Assert.Equal(["fails", "throws", "protocol-error"], calls.Select(call => call.Tool));
        Assert.Equal([ToolCallOutcome.Failed, ToolCallOutcome.Errored, ToolCallOutcome.Errored], calls.Select(call => call.Outcome));
    }

    // The record is written after the tool's own unit of work has ended, so it stays when the tool's changes roll back.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_call_whose_changes_roll_back_is_still_recorded(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration, configure: AddTestTools);
        var issued = await McpAgents.IssueAsync(host, await host.CreateFirstAdministratorAsync());
        await using var client = await McpAgents.ConnectOverHttpAsync(host, issued.Key);

        Assert.True((await client.CallToolAsync("rolls-back")).IsError);

        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>();
        Assert.False(await context.AuditEvents.AnyAsync(row => row.Operation == "test.rolled_back"));
        var call = Assert.Single(await CallsAsync(host));
        Assert.Equal("rolls-back", call.Tool);
        Assert.Equal(ToolCallOutcome.Errored, call.Outcome);
    }

    [Fact]
    public async Task A_call_over_stdio_is_recorded_with_its_agent()
    {
        using var install = new TemporaryInstall();
        var issued = await McpAgents.IssueAsync(install.Configuration);
        await using (var stdio = await InProcessStdio.StartAsync(install.Configuration, issued.Key))
        {
            await using var client = await stdio.ConnectAsync();
            await client.CallToolAsync("whoami", options: new RequestOptions
            {
                Meta = new JsonObject { [ToolCallRecording.TaskKey] = "TASK-7" },
            });
        }

        await using var services = TestServices.Build(install.Configuration);
        await using var scope = services.CreateAsyncScope();
        var call = Assert.Single(await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().ToolCalls.AsNoTracking().ToListAsync());
        Assert.Equal(issued.Credential.Id, call.AgentCredentialId);
        Assert.Equal("whoami", call.Tool);
        Assert.Equal("TASK-7", call.TaskId);
        Assert.Null(call.Model);
        Assert.Equal(ToolCallOutcome.Succeeded, call.Outcome);
    }

    private static void AddTestTools(Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(McpAgents.WhoAmI).AddSingleton(Fails).AddSingleton(Throws).AddSingleton(RollsBack).AddSingleton(ProtocolError);
    }

    // In the order the calls were made; each call is recorded before its answer goes back.
    private static async Task<List<ToolCallRow>> CallsAsync(AdminHost host)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>().ToolCalls.AsNoTracking().ToListAsync();
        return [.. rows.OrderBy(row => row.Timestamp).ThenBy(row => row.Id)];
    }
}
