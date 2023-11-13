﻿using FluentCMS.Entities;
using FluentCMS.Repositories;
using FluentCMS.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.Tests.Services;
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
            Email = "TestUser",
            UserName = username,
            RoleIds = [],
        }, "password");

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
