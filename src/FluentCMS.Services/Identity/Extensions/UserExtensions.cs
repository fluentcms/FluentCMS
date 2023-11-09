using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using uBeac.Identity;

namespace Microsoft.Extensions.DependencyInjection;

public static class UserExtensions
{
    public static IServiceCollection AddUserService<TUserService, TUserKey, TUser>(this IServiceCollection services)
        where TUserKey : IEquatable<TUserKey>
        where TUser : User<TUserKey>
        where TUserService : class, IUserService<TUserKey, TUser>
    {
        services.AddScoped<IUserService<TUserKey, TUser>, TUserService>();
        return services;
    }

    public static IServiceCollection AddUserService<TUserService, TUser>(this IServiceCollection services, IConfiguration config)
        where TUser : User
        where TUserService : class, IUserService<TUser>
    {
        services.Configure<UserRegisterOptions>(config.GetSection("UserRegisterOptions"));
        services.AddScoped<IUserService<TUser>, TUserService>();
        return services;
    }

    public static IdentityBuilder AddIdentityUser<TUserKey, TUser>(this IServiceCollection services, Action<IdentityOptions> configureIdentityOptions = default, Action<UserOptions<TUserKey, TUser>> configureOptions = default)
        where TUserKey : IEquatable<TUserKey>
        where TUser : User<TUserKey>
    {
        // Configure AspNetIdentity
        services.AddDataProtection();
        var builder = services.AddIdentityCore<TUser>(configureIdentityOptions ?? (_ => GetDefaultOptions()));
        builder
            .AddUserStore<UserStore<TUser, TUserKey>>()
            .AddUserManager<UserManager<TUser>>()
            .AddDefaultTokenProviders();

        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUserOptions(configureOptions);

            // Insert default values
            var scope = builder.Services.BuildServiceProvider().CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService<TUserKey, TUser>>();
            var userRoleService = scope.ServiceProvider.GetRequiredService<IUserRoleService<TUserKey, TUser>>();
            userService.InsertAdminUserAndAssignRole(userRoleService, options.AdminRole, options.AdminUser, options.AdminPassword);
        }

        return builder;
    }

    public static IdentityBuilder AddIdentityUser<TUser>(this IServiceCollection services, Action<IdentityOptions> configureIdentityOptions = default, Action<UserOptions<TUser>> configureOptions = default)
           where TUser : User
    {
        // Configure AspNetIdentity
        services.AddDataProtection();
        var builder = services.AddIdentityCore<TUser>(configureIdentityOptions ?? (_ => GetDefaultOptions()));
        builder
            .AddUserStore<UserStore<TUser>>()
            .AddUserManager<UserManager<TUser>>()
            .AddDefaultTokenProviders();

        if (configureOptions is not null)
        {
            // Register options
            var options = builder.Services.RegisterUserOptions(configureOptions);

            // Insert default values
            var scope = builder.Services.BuildServiceProvider().CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<IUserService<TUser>>();
            var userRoleService = scope.ServiceProvider.GetRequiredService<IUserRoleService<TUser>>();
            userService.InsertAdminUserAndAssignRole(userRoleService, options.AdminRole, options.AdminUser, options.AdminPassword);
        }

        return builder;
    }

    private static IdentityOptions GetDefaultOptions()
    {
        var options = new IdentityOptions
        {
            Password =
            {
                // Password settings
                RequireDigit = false,
                RequireLowercase = false,
                RequireNonAlphanumeric = false,
                RequireUppercase = false,
                RequiredLength = 1,
                RequiredUniqueChars = 0
            },
            Lockout =
            {
                // Lockout settings
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(1),
                MaxFailedAccessAttempts = 5,
                AllowedForNewUsers = true
            },
            User =
            {
                // User settings
                AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+",
                RequireUniqueEmail = true
            }
        };

        return options;
    }

    private static UserOptions<TUserKey, TUser> RegisterUserOptions<TUserKey, TUser>(this IServiceCollection services, Action<UserOptions<TUserKey, TUser>> configureOptions)
        where TUserKey : IEquatable<TUserKey>
        where TUser : User<TUserKey>
    {
        // Register IOptions<UserOptions<,>>
        services.Configure(configureOptions);

        // Get UserOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UserOptions<TUserKey, TUser>>>().Value;

        // Register UserOptions<,> without IOptions
        services.AddSingleton<UserOptions<TUserKey, TUser>>(options);

        return options;
    }

    private static UserOptions<TUser> RegisterUserOptions<TUser>(this IServiceCollection services, Action<UserOptions<TUser>> configureOptions)
        where TUser : User
    {
        // Register IOptions<UserOptions<,>>
        services.Configure(configureOptions);

        // Get UserOptions<,> from ServiceProvider
        var options = services.BuildServiceProvider().GetRequiredService<Options.IOptions<UserOptions<TUser>>>().Value;

        // Register UserOptions<,> without IOptions
        services.AddSingleton<UserOptions<TUser>>(options);

        return options;
    }

    private static void InsertAdminUserAndAssignRole<TUserKey, TUser>(this IUserService<TUserKey, TUser> userService, IUserRoleService<TUserKey, TUser> userRoleService, string role, TUser user, string password)
        where TUserKey : IEquatable<TUserKey>
        where TUser : User<TUserKey>
    {
        if (user is null || password is null) return;

        // If user was not inserted before, insert it
        try
        {
            if (userService.ExistsUserName(user.UserName).Result is false)
            {
                userService.Create(user, password).Wait();
                if (role is not null) userRoleService.AddRoles(user.Id, new List<string> { role }).Wait();
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private static void InsertAdminUserAndAssignRole<TUser>(this IUserService<TUser> userService, IUserRoleService<TUser> userRoleService, string role, TUser user, string password)
        where TUser : User
    {
        userService.InsertAdminUserAndAssignRole<Guid, TUser>(userRoleService, role, user, password);
    }
}