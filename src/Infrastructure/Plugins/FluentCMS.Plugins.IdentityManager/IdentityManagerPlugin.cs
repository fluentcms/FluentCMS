using FluentCMS.Configuration.Abstractions;
using FluentCMS.Repositories.EntityFramework.Configuration;

namespace FluentCMS.Plugins.IdentityManager;

public class IdentityManagerPlugin : IPlugin
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddAutoMapper(cfg => { }, typeof(MappingProfile));

        services.AddSchemaValidator<IdentitySchemaValidator, IIdentityDatabaseMarker>();
        services.AddDataSeeder<IdentityDataSeeder, IIdentityDatabaseMarker>();
        services.AddDatabaseContext<ApplicationDbContext, IIdentityDatabaseMarker>();

        // Services registration
        //services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        //services.AddScoped<IUserRoleService, UserRoleService>();

        // Repositories registration
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();

        services.AddIdentity<User, Role>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // Configure Identity options from appsettings.json
        services.AddDbOptions<JwtOptions>("JwtOptions");

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer();

        // TODO: implement here the token validation parameters configuration
        //builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, (jwtBearerOptions, sp) =>
        //{
        //    var jwtOptions = sp.GetRequiredService<IOptionsSnapshot<JwtOptions>>().Value;
        //    options.TokenValidationParameters = new TokenValidationParameters
        //    {
        //        ValidateIssuer = validationParams.ValidateIssuer,
        //        ValidateAudience = validationParams.ValidateAudience,
        //        ValidateLifetime = validationParams.ValidateLifetime,
        //        ValidateIssuerSigningKey = validationParams.ValidateIssuerSigningKey,
        //        RequireExpirationTime = validationParams.RequireExpirationTime,
        //        RequireSignedTokens = validationParams.RequireSignedTokens,
        //        ClockSkew = validationParams.ClockSkew,
        //        ValidIssuer = jwtOptions.Issuer,
        //        ValidAudience = jwtOptions.Audience,
        //        IssuerSigningKey = new SymmetricSecurityKey(SHA512.HashData(Encoding.UTF8.GetBytes(jwtOptions.Secret)))
        //    };
        //});

    }

    public void Configure(IApplicationBuilder app)
    {
    }

}
