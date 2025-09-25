using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Configuration.SqlServer;

internal class SqlServerOptionsRepository(string connectionString, ILogger<SqlServerOptionsRepository>? logger = null) : IOptionsRepository
{
    // Use NVARCHAR to store text; 450 keeps PK within SQL Server's index key size limit (900 bytes).
    private const string CreateSql = @"
            IF OBJECT_ID(N'dbo.Options', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Options
                (
                    Section NVARCHAR(450) NOT NULL PRIMARY KEY,
                    Value   NVARCHAR(MAX) NOT NULL
                );
            END
            ";

    private const string SelectAllSql = "SELECT Section, Value FROM dbo.Options;";

    // Upsert implemented as: UPDATE first; if no rows affected, INSERT.
    private const string UpdateSql = @"
            UPDATE dbo.Options
            SET Value = @value
            WHERE Section = @section;
            ";

    private const string InsertSql = @"
            INSERT INTO dbo.Options (Section, Value)
            VALUES (@section, @value);
            ";

    public async Task EnsureCreated(CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = CreateSql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Dictionary<string, string?>> GetAllSections(CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectAllSql;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var section = reader.GetString(0);
            var json = reader.GetString(1);

            try
            {
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    FlattenJson(data, section, obj);
                }
                else
                {
                    logger?.LogWarning("Configuration section '{Section}' contains non-object JSON: {Json}", section, json);
                }
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Failed to parse JSON for configuration section '{Section}': {Json}", section, json);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Unexpected error processing configuration section '{Section}'", section);
            }
        }

        return data;
    }

    public async Task<int> Upsert(OptionRegistration registration, CancellationToken cancellationToken = default)
    {
        // Security: Validate input parameters
        ValidateRegistration(registration);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        // We'll try UPDATE first; if it didn't touch anything, do INSERT.
        await using var updateCmd = conn.CreateCommand();
        updateCmd.CommandText = UpdateSql;
        AddParameters(updateCmd, registration);

        var affected = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected > 0)
            return affected;

        await using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = InsertSql;
        AddParameters(insertCmd, registration);

        try
        {
            affected = await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            return affected;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601) // PK/Unique violation
        {
            // Race: someone inserted between our UPDATE and INSERT. Treat as success (0 or 1).
            return 0;
        }
    }

    private static void ValidateRegistration(OptionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration, nameof(registration));

        // Security: Validate section name
        if (string.IsNullOrWhiteSpace(registration.Section))
        {
            throw new ArgumentException("Section name cannot be null or whitespace", nameof(registration));
        }

        // Security: Prevent excessively long section names that could cause issues
        if (registration.Section.Length > 450) // Match database constraint
        {
            throw new ArgumentException("Section name cannot exceed 450 characters", nameof(registration));
        }

        // Security: Validate section name doesn't contain dangerous characters
        if (registration.Section.Contains('\0') || registration.Section.Contains('\r') || registration.Section.Contains('\n'))
        {
            throw new ArgumentException("Section name contains invalid characters", nameof(registration));
        }

        // Security: Validate JSON content
        if (string.IsNullOrWhiteSpace(registration.DefaultValue))
        {
            throw new ArgumentException("Default value cannot be null or whitespace", nameof(registration));
        }

        // Security: Prevent excessively large JSON payloads
        if (registration.DefaultValue.Length > 1_000_000) // 1MB limit
        {
            throw new ArgumentException("Default value cannot exceed 1MB", nameof(registration));
        }

        // Security: Basic JSON validation
        try
        {
            JsonDocument.Parse(registration.DefaultValue);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Default value is not valid JSON: {ex.Message}", nameof(registration));
        }
    }

    private static void AddParameters(SqlCommand cmd, OptionRegistration reg)
    {
        // Explicit sizes/types to avoid AddWithValue pitfalls.
        var pSection = new SqlParameter("@section", SqlDbType.NVarChar, 450) { Value = reg.Section };
        var pValue = new SqlParameter("@value", SqlDbType.NVarChar, -1) { Value = reg.DefaultValue ?? (object)DBNull.Value };

        cmd.Parameters.Add(pSection);
        cmd.Parameters.Add(pValue);
    }

    private static void FlattenJson(Dictionary<string, string?> data, string prefix, JsonObject obj)
    {
        foreach (var kvp in obj)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";
            switch (kvp.Value)
            {
                case JsonObject childObj:
                    FlattenJson(data, key, childObj);
                    break;

                case JsonArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var item = arr[i];
                        if (item is JsonObject itemObj)
                        {
                            FlattenJson(data, $"{key}:{i}", itemObj);
                        }
                        else
                        {
                            data[$"{key}:{i}"] = item?.ToJsonString();
                        }
                    }
                    break;

                default:
                    data[key] = kvp.Value?.GetValueKind() is JsonValueKind.String
                        ? kvp.Value!.ToString()
                        : kvp.Value?.ToJsonString();
                    break;
            }
        }
    }
}
