namespace FluentCMS.Api.Plugins.IdentityManagement;

[Plugin]
public class IdentityManagementPlugin : IPluginStartup
{
    public int ConfigureServicesPriority => 0;  // Ensure this runs early to set up identity
    public int ConfigurePriority => 1;          // Ensure this runs early to set up identity

    public void ConfigureServices(IServiceCollection services, IConfiguration? configuration)
    {
        AddJwtAuthentication(services, configuration);

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
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ITokenGenerator, TokenGenerator>();
        services.AddScoped<IEmailSender, EmailSender>();

        // Register data seeder and schema validator
        services.AddDataSeeder<IdentityDataSeeder, IIdentityDatabaseArea>();
        services.AddSchemaValidator<IdentitySchemaValidator, IIdentityDatabaseArea>();

        services.AddScoped<SecurityContextResolver>();
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext>(sp => sp.GetRequiredService<ISecurityContext>());
        services.AddScoped(sp => sp.GetRequiredService<SecurityContextResolver>().Resolve());

        // Register database context
        services.AddDatabaseContext<AppIdentityDbContext, IIdentityDatabaseArea>();

        services.AddOptions<IdentityOptions>()
            .BindConfiguration("IdentityOptions");

        services.AddAuthorization();

        services.AddIdentity<User, Role>(options =>
        {
            var identityOptions = configuration?.GetSection("IdentityOptions").Get<IdentityOptions>();
            if (identityOptions == null)
                return;

            options.Password = identityOptions.Password;
            options.Lockout = identityOptions.Lockout;
            options.User = identityOptions.User;
            options.SignIn = identityOptions.SignIn;
            options.Tokens = identityOptions.Tokens;
            options.Stores = identityOptions.Stores;
            options.ClaimsIdentity = identityOptions.ClaimsIdentity;
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

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration? configuration)
    {
        // Configure JWT settings
        services.AddOptions<JwtOptions>()
            .BindConfiguration("JwtOptions");

        var jwtSettings = (configuration?.GetSection("JwtOptions").Get<JwtOptions>()) ??
            throw new InvalidOperationException("JWTOptions configuration section is missing or invalid.");

        var key = Encoding.UTF8.GetBytes(jwtSettings.Secret);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = false; // For development, set to true in production
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            // Custom token extraction from X-User-Token header
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = context.Request.Headers["X-User-Token"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(token))
                    {
                        context.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    }

}
