using IdentityExample.Configuration;
using IdentityExample.Services;
using IdentityExample.Profiles;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace IdentityExample.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure Entity Framework
        services.AddDbContext<AppIdentityDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));

        // Configure Identity with custom options from appsettings
        services.AddIdentity<User, Role>(options =>
        {
            var identityOptions = configuration.GetSection("IdentityOptions");
            
            // Password settings
            var passwordOptions = identityOptions.GetSection("Password");
            options.Password.RequireDigit = passwordOptions.GetValue<bool>("RequireDigit");
            options.Password.RequiredLength = passwordOptions.GetValue<int>("RequiredLength");
            options.Password.RequireNonAlphanumeric = passwordOptions.GetValue<bool>("RequireNonAlphanumeric");
            options.Password.RequireUppercase = passwordOptions.GetValue<bool>("RequireUppercase");
            options.Password.RequireLowercase = passwordOptions.GetValue<bool>("RequireLowercase");
            options.Password.RequiredUniqueChars = passwordOptions.GetValue<int>("RequiredUniqueChars");

            // Lockout settings
            var lockoutOptions = identityOptions.GetSection("Lockout");
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.Parse(lockoutOptions.GetValue<string>("DefaultLockoutTimeSpan")!);
            options.Lockout.MaxFailedAccessAttempts = lockoutOptions.GetValue<int>("MaxFailedAccessAttempts");
            options.Lockout.AllowedForNewUsers = lockoutOptions.GetValue<bool>("AllowedForNewUsers");

            // User settings
            var userOptions = identityOptions.GetSection("User");
            options.User.RequireUniqueEmail = userOptions.GetValue<bool>("RequireUniqueEmail");
            options.User.AllowedUserNameCharacters = userOptions.GetValue<string>("AllowedUserNameCharacters")!;

            // SignIn settings
            var signInOptions = identityOptions.GetSection("SignIn");
            options.SignIn.RequireConfirmedEmail = signInOptions.GetValue<bool>("RequireConfirmedEmail");
            options.SignIn.RequireConfirmedPhoneNumber = signInOptions.GetValue<bool>("RequireConfirmedPhoneNumber");
            options.SignIn.RequireConfirmedAccount = signInOptions.GetValue<bool>("RequireConfirmedAccount");
        })
        .AddEntityFrameworkStores<AppIdentityDbContext>()
        .AddDefaultTokenProviders();

        return services;
    }

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure JWT settings
        services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));
        
        var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
        var key = Encoding.UTF8.GetBytes(jwtSettings.SecretKey);

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

        return services;
    }

    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure SMTP settings
        services.Configure<SmtpSettings>(configuration.GetSection("SmtpSettings"));
        
        // Configure token cleanup settings
        services.Configure<TokenCleanupSettings>(configuration.GetSection("TokenCleanupSettings"));

        // Register application services
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        
        // Add HTTP context accessor for CurrentUserService
        services.AddHttpContextAccessor();

        // Add AutoMapper
        services.AddAutoMapper(cfg => 
        {
            cfg.AddProfile<AutoMapperProfile>();
        });

        return services;
    }
}
