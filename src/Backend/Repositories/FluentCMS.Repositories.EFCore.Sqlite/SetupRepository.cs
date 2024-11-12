using FluentCMS.Repositories.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace FluentCMS.Repositories.EFCore.Sqlite;

public class SetupRepository(FluentCmsDbContext dbContext) : ISetupRepository
{
    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        // Check if the database exists by querying any required table or checking metadata
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            return false;

        // Check if the GlobalSettings table exists
        var tableExists = await dbContext.Database.ExecuteSqlRawAsync(@"SELECT count(name) FROM sqlite_master WHERE type='table' AND name='GlobalSettings';", cancellationToken: cancellationToken) > 0;

        return tableExists;
    }

    public async Task<bool> InitializeDb(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if the database is already initialized
            if (await Initialized(cancellationToken))
                return false; // Database is already initialized

            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "FluentCMS.Repositories.EFCore.Sqlite.Setup.sql";

            using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException($"Resource '{resourceName}' not found.");

            using var reader = new StreamReader(stream);
            var sqlScript = await reader.ReadToEndAsync(cancellationToken);

            // Execute the SQL script
            await dbContext.Database.ExecuteSqlRawAsync(sqlScript, cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize the database.", ex);
        }
    }
}
