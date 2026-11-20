using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Providers;

public sealed class TestDatabaseTests
{
    [Fact]
    public void ADatabaseNotForSystemTablesIsEmptyEvenAfterAMigratedOne()
    {
        using (var migrated = TestDatabase.Create(forSystemTables: true))
        {
            if (TestProvider.IsPostgreSql)
            {
                migrated.Tables().ShouldNotBeEmpty();
            }
        }

        using var empty = TestDatabase.Create();

        empty.Tables().ShouldBeEmpty();
    }

    [PostgreSqlFact("copies of its template")]
    public void TheRunsTemplateIsMigratedOnceAndEachTestGetsACopyOfItsOwn()
    {
        using var first = TestDatabase.Create(forSystemTables: true);
        using var second = TestDatabase.Create(forSystemTables: true);

        first.Execute("create table scratch (id int)");

        DatabaseServer.Template.Migrations.ShouldBe(1);
        PendingSystemMigrations(first.ConnectionString).ShouldBeEmpty();
        PendingSystemMigrations(second.ConnectionString).ShouldBeEmpty();
        first.Tables().ShouldContain("scratch");
        second.Tables().ShouldNotContain("scratch");
    }

    [PostgreSqlFact("copies of its template")]
    public async Task TestsThatAskTogetherMigrateANewTemplateOnce()
    {
        var template = new PostgreSqlTemplate(DatabaseServer.AdminConnectionString, $"template_{Guid.NewGuid():N}");
        var copies = new ConcurrentBag<string>();
        using var start = new ManualResetEventSlim();
        var tests = Enumerable.Range(0, 8)
            .Select(_ => Task.Factory.StartNew(() =>
            {
                start.Wait();
                copies.Add(template.Copy());
            }, TaskCreationOptions.LongRunning))
            .ToList();

        start.Set();
        try
        {
            await Task.WhenAll(tests);

            template.Migrations.ShouldBe(1);
            copies.Distinct().Count().ShouldBe(8);
        }
        finally
        {
            foreach (var copy in copies)
            {
                DatabaseServer.DropDatabase(copy);
            }

            DatabaseServer.DropDatabase(template.Name);
        }
    }

    private static List<string> PendingSystemMigrations(string connectionString)
    {
        using var context = PostgreSqlTemplate.SystemContext(connectionString);
        return [.. context.Database.GetPendingMigrations()];
    }
}
