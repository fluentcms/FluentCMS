using FluentCMS.Entities.Users;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests;
public class UserServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    public UserServiceTests()
    {
        var services = new ServiceCollection();
        services.AddFluentCMSCore()
            .AddLiteDbRepository(b => b.UseInMemory());
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_Create()
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<Services.UserService>();

        var username = "testuser";
        var userToCreate = new User
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = "TestUser",
            Username = username,
            Password = "password",
        };
        await userService.Create(userToCreate, Enumerable.Empty<Guid>());

        var loadedUser = await userService.GetByUsername(username);
        loadedUser.ShouldNotBeNull();
        loadedUser.Id.ShouldBe(userToCreate.Id);
    }

    [Fact]
    public async Task Should_Not_Create_Unprovided_Username()
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<Services.UserService>();

        var userToCreate = new User
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            CreatedBy = "",
            LastUpdatedBy = "",
            Name = "TestUser",
            Username = "",
            Password = "password",
        };

        // it should throw a ApplicationException
        await Should.ThrowAsync<ApplicationException>(() => userService.Create(userToCreate, Enumerable.Empty<Guid>()));
    }
}
