using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
    
namespace FluentCMS.Tests.Services;
public class UserServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    public UserServiceTests()
    {
        var configurationBuilder = new ConfigurationBuilder();
        //"Jwt": {
        //  "Issuer": "https://localhost:44352",
        //  "Audience": "https://localhost:44352",
        //  "TokenExpiry": 3600, // seconds
        //  "RefreshTokenExpiry": 36000, // seconds
        //  "Secret": "THIS SHOULD BE A LONG COMPLEX SECRET KEY, CHANGE THIS IN PRODUCTION!"
        //}
        configurationBuilder.AddInMemoryCollection(new KeyValuePair<string, string?>[]
        {
            new ("JWT:Issuer","https://localhost:44352"),
            new ("JWT:Audience","https://localhost:44352"),
            new ("JWT:TokenExpiry","3600"),
            new ("JWT:RefreshTokenExpiry","36000"),
            new ("JWT:Secret","THIS SHOULD BE A LONG COMPLEX SECRET KEY, CHANGE THIS IN PRODUCTION!"),
        }) ;
        var configuration = configurationBuilder.Build();
        var services = new ServiceCollection();
        services
            .AddApplicationServices()
            .AddInMemoryLiteDbRepositories();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwtTokenProvider(configuration);
        services.AddDataProtection();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_Create()
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var username = "testuser";
        var createdUser = await userService.Create(new User
        {
            Id = Guid.NewGuid(),
            Email = "TestUser",
            UserName = username,
            RoleIds = [],
        }, "1234Abcd%");

        var loadedUser = await userService.GetByUsername(username);
        loadedUser.ShouldNotBeNull();
        loadedUser.Id.ShouldBe(createdUser.Id);
    }

    [Fact]
    public async Task Should_Not_Create_Unprovided_Username()
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var userToCreate = new User
        {
            Id = Guid.NewGuid(),
            Email = "TestUser",
            UserName = "testuser",
            RoleIds = [],
        };

        // it should throw a ApplicationException
        await Should.ThrowAsync<ApplicationException>(() => userService.Create(userToCreate, "password"));
    }
}
