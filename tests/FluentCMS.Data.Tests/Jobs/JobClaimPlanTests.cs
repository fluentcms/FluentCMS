using System.Data.Common;
using FluentCMS.Core.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Jobs;

public sealed class JobClaimPlanTests : IDisposable
{
    private readonly JobFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
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
