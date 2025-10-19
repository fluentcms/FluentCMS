namespace FluentCMS.Api.Plugins.IdentityManagement.Repositories;

internal class IdentityDataSeeder(AppIdentityDbContext dbContext, UserManager<User> userManager, ILogger<IdentityDataSeeder> logger) : BaseDataSeeder<AppIdentityDbContext>(dbContext, logger)
{
    public override int Priority => 0; // Set priority to 0 to run before other seeders

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Seeding initial identity data into the database...");
        var user = new User
        {
            UserName = "superadmin",
            Email = "admin@example.com",
            IsSuperAdmin = true,
            EmailConfirmed = true,
            Suspended = false,
            Description = "Default super admin user with full access, change password after first login."
        };

        var result = await userManager.CreateAsync(user, "Admin@12345");
        if (result.Succeeded)
        {
            logger.LogInformation("Identity data seeding completed.");
        }
        else
        {
            logger.LogError("Failed to create superadmin user: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            throw new Exception("Failed to create superadmin user.");
        }
    }
}
