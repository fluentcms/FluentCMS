using System.Data.Common;
using FluentCMS.Core.Jobs;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Jobs;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

public sealed class JobClaimPlanTests : IDisposable
{
    private readonly JobFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [SqliteFact("SQLite's query plan")]
    public async Task TheClaimSeeksItsIndexWithoutASort()
    {
        var faults = new JobFaults();
        var store = _fixture.NewInstance(faults: faults).GetRequiredService<JobStore>();
        await _fixture.EnqueueAsync();

        // Asks the database about the statement the dialect really sends, with what it really binds.
        string? sql = null;
        List<SqliteParameter> parameters = [];
        faults.Fail(command =>
        {
            if (sql is null && command.CommandText.Contains("ORDER BY priority DESC", StringComparison.Ordinal))
            {
                sql = command.CommandText;
                parameters = [.. command.Parameters.Cast<DbParameter>().Select(parameter => new SqliteParameter(parameter.ParameterName, parameter.Value))];
            }

            return false;
        });

        await store.ClaimAsync(JobRunnerOptions.DefaultQueue, "A", TestContext.Current.CancellationToken);

        var plan = ExplainQueryPlan(sql.ShouldNotBeNull(), parameters);
        plan.ShouldContain(step => step.Contains("ix_cms_job_claim", StringComparison.Ordinal));
        plan.ShouldNotContain(step => step.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }

    // A sort under UPDLOCK would lock every due row it reads, and concurrent claimers would skip them all (#2711).
    [SqlServerFact("SQL Server's query plan")]
    public async Task OnSqlServerTheClaimSeeksItsIndexWithoutASort()
    {
        var faults = new JobFaults();
        var store = _fixture.NewInstance(faults: faults).GetRequiredService<JobStore>();
        foreach (var index in Enumerable.Range(0, 200))
        {
            await _fixture.EnqueueAsync($"job-{index}", new JobOptions { Priority = index % 3 });
        }

        string? sql = null;
        List<SqlParameter> parameters = [];
        faults.Fail(command =>
        {
            if (sql is null && command.CommandText.Contains("ORDER BY priority DESC", StringComparison.Ordinal))
            {
                sql = command.CommandText;
                parameters = [.. command.Parameters.Cast<SqlParameter>().Select(parameter => new SqlParameter(parameter.ParameterName, parameter.SqlDbType, parameter.Size) { Value = parameter.Value, Precision = parameter.Precision, Scale = parameter.Scale })];
            }

            return false;
        });

        await store.ClaimAsync(JobRunnerOptions.DefaultQueue, "A", TestContext.Current.CancellationToken);

        var plan = ActualPlan(sql.ShouldNotBeNull(), parameters);
        plan.ShouldContain("Index=\"[ix_cms_job_claim]\"");
        plan.ShouldNotContain("PhysicalOp=\"Sort\"");
    }

    // The plan SQL Server ran, with what the dialect bound.
    private string ActualPlan(string sql, List<SqlParameter> parameters)
    {
        // Not pooled, so that no later command of the test gets a plan with its rows.
        using var connection = new SqlConnection(new SqlConnectionStringBuilder(_fixture.Database.ConnectionString) { Pooling = false }.ConnectionString);
        connection.Open();
        using (var statistics = new SqlCommand("SET STATISTICS XML ON", connection))
        {
            statistics.ExecuteNonQuery();
        }

#pragma warning disable CA2100 // The statement of the dialect under test.
        using var command = new SqlCommand(sql, connection);
#pragma warning restore CA2100
        command.Parameters.AddRange([.. parameters]);
        using var reader = command.ExecuteReader();
        do
        {
            if (reader.GetName(0).Contains("Showplan", StringComparison.Ordinal) && reader.Read())
            {
                return reader.GetString(0);
            }
        }
        while (reader.NextResult());

        throw new InvalidOperationException("SQL Server returned no plan.");
    }

    private List<string> ExplainQueryPlan(string sql, List<SqliteParameter> parameters)
    {
        using var connection = new SqliteConnection(_fixture.Database.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // The statement of the dialect under test.
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
#pragma warning restore CA2100
        command.Parameters.AddRange(parameters);

        using var reader = command.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }
}
