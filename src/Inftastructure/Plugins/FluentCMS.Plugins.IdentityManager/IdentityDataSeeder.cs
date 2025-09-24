namespace FluentCMS.Plugins.IdentityManager;

public class IdentityDataSeeder(ApplicationDbContext dbContext, IDatabaseManager<IIdentityDatabaseMarker> databaseManager) : IDataSeeder
{
    public int Priority => 1000;

    public async Task<bool> HasData(CancellationToken cancellationToken = default)
    {
        return !await databaseManager.TablesEmpty(["Roles"], cancellationToken);
    }

    public async Task SeedData(CancellationToken cancellationToken = default)
    {
        var roles = new[]
        {
            new Role { Name = "Admin", NormalizedName = "ADMIN" },
            new Role { Name = "User", NormalizedName = "USER" }
        };
        await dbContext.Roles.AddRangeAsync(roles);
        await dbContext.SaveChangesAsync(cancellationToken);

    }
}
