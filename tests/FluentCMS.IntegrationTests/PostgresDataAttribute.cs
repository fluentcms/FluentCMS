using System.Reflection;
using FluentCMS.Infrastructure.Persistence;
using Xunit.Sdk;

namespace FluentCMS.IntegrationTests;

// The PostgreSQL row of a theory that takes a provider name; skipped, like PostgresFact, when no connection string is set.
public sealed class PostgresDataAttribute : DataAttribute
{
    public PostgresDataAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFactAttribute.VariableName)))
        {
            Skip = $"{PostgresFactAttribute.VariableName} is not set.";
        }
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        return [[DatabaseProviders.PostgreSql]];
    }
}
