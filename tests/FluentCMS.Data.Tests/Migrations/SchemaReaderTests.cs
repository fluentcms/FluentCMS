using FluentCMS.Core.Storage;
using FluentCMS.Data.Tests.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Migrations;

// The schema reader of the provider of the run, on a table made by hand. The baseline check finds an index of the model by
// its name, so an index whose keys are expressions has to be listed as one of columns is (#3530).
public sealed class SchemaReaderTests : IDisposable
{
    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    // SQL Server indexes no expression, only a computed column, which is a column of the table, and a filtered index there
    // cannot hold one, so its filtered index is on a plain column.
    [Fact]
    public async Task EachReaderListsAnIndexOfExpressionsByItsName()
    {
        (string[] Statements, string[] Expected) table = TestProvider.Name switch
        {
            TestProvider.Sqlite => (
                [
                    "create table tests_reader (id integer primary key, name text)",
                    "create index ix_tests_reader_lower on tests_reader (lower(name))",
                    "create unique index ux_tests_reader_name_lower on tests_reader (name, lower(name)) where name is not null",
                ],
                ["ix_tests_reader_lower::False:False", "ux_tests_reader_name_lower:name,:True:True"]),
            TestProvider.PostgreSql => (
                [
                    "create table tests_reader (id integer primary key, name text)",
                    "create index ix_tests_reader_lower on tests_reader (lower(name))",
                    "create unique index ux_tests_reader_name_lower on tests_reader (name, lower(name)) where name is not null",
                    "create index gin_tests_reader_text on tests_reader using gin (to_tsvector('simple'::regconfig, name))",
                ],
                ["gin_tests_reader_text::False:False", "ix_tests_reader_lower::False:False", "ux_tests_reader_name_lower:name,:True:True"]),
            _ => (
                [
                    "create table tests_reader (id int primary key, name nvarchar(100), lowered as lower(name))",
                    "create index ix_tests_reader_lower on tests_reader (lowered)",
                    "create unique index ux_tests_reader_name on tests_reader (name) where name is not null",
                ],
                ["ix_tests_reader_lower:lowered:False:False", "ux_tests_reader_name:name:True:True"]),
        };

        foreach (var statement in table.Statements)
        {
            _database.Execute(statement);
        }

        using var host = _database.CreateHost(ArticleSchemas.NoTypes, "Production");
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        await context.Database.OpenConnectionAsync(Token);
        var database = await scope.ServiceProvider.GetRequiredService<IDataProvider>().SchemaReader.ReadAsync(context.Database.GetDbConnection(), Token);

        database.Tables["tests_reader"].Indexes
            .Select(index => $"{index.Name}:{string.Join(",", index.Columns)}:{index.IsUnique}:{index.IsPartial}")
            .Order(StringComparer.Ordinal)
            .ShouldBe(table.Expected);
    }
}
