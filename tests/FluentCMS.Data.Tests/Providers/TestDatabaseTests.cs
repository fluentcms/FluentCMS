using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        using var third = TestDatabase.Create(forSystemTables: true);

        DatabaseServer.Template.Migrations.ShouldBe(1);
        PendingSystemMigrations(first.ConnectionString).ShouldBeEmpty();
        PendingSystemMigrations(second.ConnectionString).ShouldBeEmpty();
        PendingSystemMigrations(third.ConnectionString).ShouldBeEmpty();
        first.Tables().ShouldContain("scratch");
        second.Tables().ShouldNotContain("scratch");
        third.Tables().ShouldNotContain("scratch");
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

    [PostgreSqlFact("copies of its template")]
    public void AFailedMigrationDropsTheTemplateAndFailsEveryLaterCopyAtOnce()
    {
        var attempts = 0;
        var template = new PostgreSqlTemplate(DatabaseServer.AdminConnectionString, $"template_{Guid.NewGuid():N}", connectionString =>
        {
            attempts++;
            DatabaseServer.Run(new NpgsqlConnection(connectionString), "create table half (id int)");
            throw new InvalidOperationException("The migration failed.");
        });

        try
        {
            var first = Should.Throw<InvalidOperationException>(template.Copy);
            var second = Should.Throw<InvalidOperationException>(template.Copy);

            first.Message.ShouldBe("The migration failed.");
            second.Message.ShouldContain(template.Name);
            second.InnerException.ShouldBeSameAs(first);
            attempts.ShouldBe(1);
            template.Migrations.ShouldBe(0);
            Exists(template.Name).ShouldBeFalse();
        }
        finally
        {
            DatabaseServer.DropDatabase(template.Name);
        }
    }

    private static bool Exists(string database)
    {
        using var connection = new NpgsqlConnection(DatabaseServer.AdminConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select count(*) from pg_database where datname = @name";
        command.Parameters.AddWithValue("name", database);
        return (long)command.ExecuteScalar()! > 0;
    }

    private static List<string> PendingSystemMigrations(string connectionString)
    {
        using var context = PostgreSqlTemplate.SystemContext(connectionString);
        return [.. context.Database.GetPendingMigrations()];
    }
}
