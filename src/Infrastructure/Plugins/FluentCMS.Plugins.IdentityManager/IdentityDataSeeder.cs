//namespace FluentCMS.Plugins.IdentityManager;

//internal class IdentityDataSeeder(ApplicationDbContext dbContext, ILogger<IdentityDataSeeder> logger) : DataSeeder<ApplicationDbContext>(dbContext, logger)
//{
//    public override int Priority => 1000;

//    public override async Task SeedData(CancellationToken cancellationToken = default)
//    {
//        var roles = new[]
//        {
//            new Role { Name = "Admin", NormalizedName = "ADMIN" },
//            new Role { Name = "User", NormalizedName = "USER" }
//        };
//        await DbContext.Roles.AddRangeAsync(roles);
//        await DbContext.SaveChangesAsync(cancellationToken);

//    }
//}
