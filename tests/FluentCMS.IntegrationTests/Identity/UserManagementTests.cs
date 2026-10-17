using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed class UserManagementTests
{
    private static readonly NewUser Owner = new("owner@example.com", "Owner", "Str0ng!Passw0rd");
    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");

    [Fact]
    public async Task The_first_administrator_is_created_once_and_holds_the_administrator_role()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = TestServices.Build(database.Configuration);
        await TestServices.MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IFirstRunSetup>();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        Assert.True(await setup.NeedsFirstAdministratorAsync());

        var created = await setup.CreateFirstAdministratorAsync(Owner);

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        Assert.False(await setup.NeedsFirstAdministratorAsync());

        var again = await setup.CreateFirstAdministratorAsync(Editor);
        Assert.Equal("already_set_up", Assert.Single(again.Errors).Code);

        var owner = Assert.Single(await directory.ListAsync());
        Assert.Equal(created.Value, owner.Id);
        Assert.Equal(Owner.Email, owner.Email);
        Assert.Contains(FluentCmsRoles.Administrator, owner.Roles);
        Assert.False(owner.IsDisabled);
    }

    [Fact]
    public async Task Users_can_be_created_listed_and_disabled()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = TestServices.Build(database.Configuration);
        await TestServices.MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var owner = await directory.CreateAsync(Owner);
        var editor = await directory.CreateAsync(Editor);
        Assert.True(owner.IsSuccess);
        Assert.True(editor.IsSuccess);

        var disabled = await directory.DisableAsync(editor.Value);
        Assert.True(disabled.IsSuccess);

        var users = await directory.ListAsync();
        Assert.Equal([Editor.Email, Owner.Email], users.Select(user => user.Email));
        Assert.True(users.Single(user => user.Id == editor.Value).IsDisabled);
        Assert.False(users.Single(user => user.Id == owner.Value).IsDisabled);
        Assert.All(users, user => Assert.Contains(FluentCmsRoles.Administrator, user.Roles));

        var missing = await directory.DisableAsync(Guid.NewGuid());
        Assert.Equal("user_not_found", Assert.Single(missing.Errors).Code);
    }

    [Fact]
    public async Task A_weak_password_is_rejected_with_identity_error_codes()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = TestServices.Build(database.Configuration);
        await TestServices.MigrateAsync(services);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var result = await directory.CreateAsync(Owner with { Password = "short" });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "PasswordTooShort");
        Assert.Empty(await directory.ListAsync());
    }
}
