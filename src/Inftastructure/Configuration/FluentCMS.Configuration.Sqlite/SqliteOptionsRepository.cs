using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FluentCMS.Configuration.Sqlite;

internal class SqliteOptionsRepository(string connectionString, ILogger<SqliteOptionsRepository>? logger = null) : IOptionsRepository
{
    private const string CreateSql = @"CREATE TABLE IF NOT EXISTS Options (
            Section TEXT PRIMARY KEY,
            Value    TEXT NOT NULL
        );";

    private const string SelectAllSql = "SELECT * FROM Options;";

    private const string UpsertSql = @"INSERT INTO Options (Section, Value)
            VALUES ($section, $value)
            ON CONFLICT(Section) DO NOTHING;";


    public async Task EnsureCreated(CancellationToken cancellationToken = default)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CreateSql;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Dictionary<string, string?>> GetAllSections(CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectAllSql;
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (reader.Read())
        {
            var section = reader.GetString(0);
            var json = reader.GetString(1);

            // Flatten JSON into config keys: Section:Child:SubChild = value
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

        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = UpsertSql;
        cmd.Parameters.AddWithValue("$section", registration.Section);
        cmd.Parameters.AddWithValue("$value", registration.DefaultValue);
        var affetedRows = await cmd.ExecuteNonQueryAsync(cancellationToken);

        return affetedRows;
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
