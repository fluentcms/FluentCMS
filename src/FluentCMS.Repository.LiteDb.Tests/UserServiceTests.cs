using FluentCMS.Application.Dtos.Users;
using FluentCMS.Application.Services;
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
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var username = "testuser";
        var createdUserId = await userService.Create(new CreateUserRequest
        {
            Name = "TestUser",
            Username = username,
            Password = "password",
            Roles = []
        });

        var loadedUser = await userService.GetByUsername(username);
        loadedUser.ShouldNotBeNull();
        loadedUser.Id.ShouldBe(createdUserId);
    }

    [Fact]
    public async Task Should_Not_Create_Unprovided_Username()
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();

        var userToCreate = new CreateUserRequest
        {
            Name = "TestUser",
            Username = "testuser",
            Password = "password",
            Roles = []
        };

        // it should throw a ApplicationException
        await Should.ThrowAsync<ApplicationException>(() => userService.Create(userToCreate));
    }
}
