using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FluentCMS.Configuration.SqlServer;

public static class SqlServerExtensions
{
    public static void AddSqlServerOptions(this IHostApplicationBuilder builder, string connectionString, long? reloadInterval = null)
    {
        // Security: Validate and sanitize connection string
        var validatedConnectionString = ValidateConnectionString(connectionString);

        // Security: Validate reload interval bounds
        ValidateReloadInterval(reloadInterval);

        DbConfigurationSource configSource = default!;

        builder.Configuration.Add<DbConfigurationSource>(source =>
        {
            configSource = source;

            source.Repository = new SqlServerOptionsRepository(validatedConnectionString);
            if (reloadInterval.HasValue)
                source.ReloadInterval = TimeSpan.FromSeconds(reloadInterval.Value);
        });

        builder.Services.AddSingleton(sp => configSource);
    }

    private static string ValidateConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

        // Security: Remove potentially dangerous keywords from connection string
        var dangerousKeywords = new[] { "EXEC", "EXECUTE", "SP_", "XP_", "OPENROWSET", "OPENDATASOURCE", "BULK", "SHUTDOWN" };
        var upperConnectionString = connectionString.ToUpperInvariant();

        foreach (var keyword in dangerousKeywords)
        {
            if (upperConnectionString.Contains(keyword))
            {
                throw new ArgumentException($"Connection string contains potentially dangerous keyword: {keyword}", nameof(connectionString));
            }
        }

        // Security: Ensure connection string doesn't contain suspicious patterns
        if (connectionString.Contains("--") || connectionString.Contains("/*") || connectionString.Contains("*/") || connectionString.Contains(";"))
        {
            throw new ArgumentException("Connection string contains suspicious patterns", nameof(connectionString));
        }

        // Security: Validate that connection string follows expected format
        if (!connectionString.Contains("Server=") && !connectionString.Contains("Data Source="))
        {
            throw new ArgumentException("Connection string must contain Server or Data Source parameter", nameof(connectionString));
        }

        return connectionString;
    }

    private static void ValidateReloadInterval(long? reloadInterval)
    {
        if (reloadInterval.HasValue)
        {
            // Security: Prevent extremely short intervals that could cause DoS
            const long MinIntervalSeconds = 5;
            const long MaxIntervalSeconds = 86400; // 24 hours

            if (reloadInterval.Value < MinIntervalSeconds)
            {
                throw new ArgumentException($"Reload interval must be at least {MinIntervalSeconds} seconds", nameof(reloadInterval));
            }

            if (reloadInterval.Value > MaxIntervalSeconds)
            {
                throw new ArgumentException($"Reload interval cannot exceed {MaxIntervalSeconds} seconds", nameof(reloadInterval));
            }
        }
    }
}
