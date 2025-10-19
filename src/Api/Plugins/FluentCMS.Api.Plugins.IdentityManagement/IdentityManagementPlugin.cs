namespace FluentCMS.Api.Plugins.IdentityManagement;

[Plugin]
public class IdentityManagementPlugin : IPluginStartup
{
    public int ConfigureServicesPriority => 0;  // Ensure this runs early to set up identity
    public int ConfigurePriority => 1;          // Ensure this runs early to set up identity

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        // Register auto-mapper profiles
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<MappingProfile>();
        });

        // Register repositories
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        // Register services
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserService, UserService>();

        // Register data seeder and schema validator
        services.AddDataSeeder<IdentityDataSeeder, IIdentityDatabaseArea>();
        services.AddSchemaValidator<IdentitySchemaValidator, IIdentityDatabaseArea>();

        // Replace the default role validator with our site-scoped one
        services.AddScoped<IRoleValidator<Role>, SiteScopedRoleValidator>();
        services.AddScoped<SecurityContextResolver>();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<ISecurityContext>());
        services.AddScoped(sp => sp.GetRequiredService<SecurityContextResolver>().Resolve());

        // Register database context
        services.AddDatabaseContext<AppIdentityDbContext, IIdentityDatabaseArea>();

        services.AddOptions<JwtOptions>()
            .BindConfiguration("JwtOptions");

        services.AddAuthorization();

        services.AddIdentity<User, Role>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
            options.SignIn.RequireConfirmedEmail = false;
        })
            .AddEntityFrameworkStores<AppIdentityDbContext>()
            .AddDefaultTokenProviders();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.UseAuthentication();

        app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), app =>
        {
            // this will be executed only when the path starts with "/api"
            app.UseMiddleware<JwtAuthorizationMiddleware>();
        });

        app.UseAuthorization();
    }
}
