namespace FluentCMS.IntegrationTests;

// Runs only when the variable holds a PostgreSQL connection string; CI sets it from a service container.
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string VariableName = "FLUENTCMS_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(VariableName)))
        {
            Skip = $"{VariableName} is not set.";
        }
    }
}
