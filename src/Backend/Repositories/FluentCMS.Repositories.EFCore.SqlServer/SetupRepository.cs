using FluentCMS.Repositories.Abstractions;
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

            // Load the SQL script from the embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "FluentCMS.Repositories.EFCore.SqlServer.Setup.sql";

            using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException($"Resource '{resourceName}' not found.");

            using var reader = new StreamReader(stream);
            var sqlScript = await reader.ReadToEndAsync(cancellationToken);

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
}
