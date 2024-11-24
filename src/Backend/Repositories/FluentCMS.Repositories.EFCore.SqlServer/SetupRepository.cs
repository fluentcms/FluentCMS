using FluentCMS.Repositories.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace FluentCMS.Repositories.EFCore.SqlServer;

public class SetupRepository(FluentCmsDbContext dbContext) : ISetupRepository
{
    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        // Check if the database exists by querying any required table or checking metadata
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            return false;

        // Check if the GlobalSettings table exists
        var commandText = @"
            SELECT COUNT(*) 
            FROM INFORMATION_SCHEMA.TABLES 
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'GlobalSettings';";

        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText = commandText;
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0) > 0;
    }

    public async Task<bool> InitializeDb(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the database is already initialized
            if (await Initialized(cancellationToken))
                return false; // Database is already initialized

            // Ensure the database exists using a SQL command
            var connectionString = dbContext.Database.GetConnectionString() ??
                throw new InvalidOperationException("Connection string not found.");

            var databaseName = ExtractDatabaseName(connectionString);

            if (string.IsNullOrEmpty(databaseName))
                throw new InvalidOperationException("Unable to extract database name from the connection string.");

            // Use master database to check and create the target database
            var masterConnectionString = ReplaceDatabaseName(connectionString, "master");

            await using var masterConnection = new SqlConnection(masterConnectionString);
            await masterConnection.OpenAsync(cancellationToken);

            var checkDbQuery = $@"
                IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = '{databaseName}')
                BEGIN
                    CREATE DATABASE [{databaseName}]
                END";

            await using var command = masterConnection.CreateCommand();
            command.CommandText = checkDbQuery;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // Load the SQL script from the embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "FluentCMS.Repositories.EFCore.SqlServer.Setup.sql";

            using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException($"Resource '{resourceName}' not found.");

            using var reader = new StreamReader(stream);
            var sqlScript = await reader.ReadToEndAsync(cancellationToken);

            // wait until the dbContext is ready
            while (!await dbContext.Database.CanConnectAsync(cancellationToken))
                await Task.Delay(1000, cancellationToken);

            // Split the script into individual commands
            foreach (var sqlCommand in sqlScript.Split("GO", StringSplitOptions.RemoveEmptyEntries))
            {
                // Execute the SQL script
                await dbContext.Database.ExecuteSqlRawAsync(sqlCommand, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize the database.", ex);
        }
    }

    private static string ExtractDatabaseName(string connectionString)
    {
        // Extract the database name from the connection string
        var builder = new SqlConnectionStringBuilder(connectionString);
        return builder.InitialCatalog;
    }

    private string ReplaceDatabaseName(string connectionString, string newDatabaseName)
    {
        // Replace the database name in the connection string
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = newDatabaseName
        };
        return builder.ConnectionString;
    }
}
