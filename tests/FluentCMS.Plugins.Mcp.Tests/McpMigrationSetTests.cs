using FluentCMS.Core.Plugins;
using FluentCMS.Plugins.Tests;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The MCP stream holds a migration set per provider (#2711, #2980). The sets are checked without a database, and the
// SQLite set is applied by a host, as every host of these tests applies it.
public sealed class McpMigrationSetTests
{
    public static TheoryData<string> Sets => PluginMigrationSetChecks.Sets;

    [Theory]
    [MemberData(nameof(Sets))]
    public void EachSetMatchesTheModelOnItsProviderAndRepeatsTheSqliteSet(string set)
    {
        using var context = new McpDbContextFactory().CreateDbContext([set]);
        using var sqlite = new McpDbContextFactory().CreateDbContext([PluginMigrationSets.Sqlite]);

        context.Database.GetMigrations().Select(id => id[15..]).ShouldBe(["Initial"]);
        PluginMigrationSetChecks.IsTheSetOf(context, set);
        PluginMigrationSetChecks.MatchesTheSqliteSet(context, sqlite);
    }

    [Theory]
    [MemberData(nameof(Sets))]
    public void AHostTakesTheSetOfItsProvider(string set)
    {
        using var services = PluginMigrationSetChecks.Host(set, cms => cms.AddPlugin<McpPlugin>());
        using var scope = services.CreateScope();

        PluginMigrationSetChecks.IsTheSetOf(scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>(), set);
    }

    [Fact]
    public async Task TheSqliteSetAppliesAndASiteHoldsOneRowOfGuidelinesOneOfInstructionsPerTypeAndOnePromptPerName()
    {
        await using var host = McpSiteTesting.Host();
        var site = Guid.CreateVersion7();
        var other = Guid.CreateVersion7();

        await SaveAsync(host, Instruction(site, null), Instruction(site, "article"), Instruction(site, "page"), Instruction(other, null), Instruction(other, "article"));
        await SaveAsync(host, Prompt(site, "summarize"), Prompt(site, "review"), Prompt(other, "summarize"));

        await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(host, Instruction(site, null)));
        await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(host, Instruction(site, "article")));
        await Should.ThrowAsync<DbUpdateException>(() => SaveAsync(host, Prompt(site, "summarize")));

        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
        (await database.Database.GetAppliedMigrationsAsync(Token)).ShouldHaveSingleItem().ShouldEndWith("_Initial");
        (await database.Set<McpInstruction>().CountAsync(instruction => instruction.SiteId == site, Token)).ShouldBe(3);
        (await database.Set<McpPrompt>().CountAsync(prompt => prompt.SiteId == site, Token)).ShouldBe(2);
    }

    private static McpInstruction Instruction(Guid site, string? type) =>
        new() { Id = Guid.CreateVersion7(), SiteId = site, Type = type, Text = "Write plainly." };

    private static McpPrompt Prompt(Guid site, string name) =>
        new() { Id = Guid.CreateVersion7(), SiteId = site, Name = name, Template = "Sum {{entry}} up." };

    private static async Task SaveAsync(FluentCmsTestHost host, params object[] rows)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PluginDbContext<McpPlugin>>();
        database.AddRange(rows);
        await database.SaveChangesAsync(Token);
    }
}
