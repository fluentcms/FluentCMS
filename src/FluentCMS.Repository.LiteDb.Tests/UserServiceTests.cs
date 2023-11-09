using FluentCMS.Entities;
using FluentCMS.Repositories;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Repository.LiteDb.Tests;
public class UserServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    public UserServiceTests()
    {
        var services = new ServiceCollection();
        services
            .AddApplicationServices();
            //.AddLiteDbInMemoryRepository();
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
            Name = "TestUser",
            Username = username,
            Password = "password",
            UserRoles = [],
        });

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
            Name = "TestUser",
            Username = "testuser",
            Password = "password",
            UserRoles = []
        };

        // it should throw a ApplicationException
        await Should.ThrowAsync<ApplicationException>(() => userService.Create(userToCreate));
    }
}
