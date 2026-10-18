using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Identity;

public sealed class UserManagementTests
{
    private static readonly NewUser Owner = new("owner@example.com", "Owner", "Str0ng!Passw0rd");
    private static readonly NewUser Editor = new("editor@example.com", "Editor", "An0ther!Passw0rd");
    private static readonly UserInitiator Administrator = new(Guid.NewGuid(), "Administrator");

    [Fact]
    public async Task The_first_administrator_is_created_once_and_holds_the_administrator_role()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, initiator: null);
        await using var scope = services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IFirstRunSetup>();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        Assert.True(await setup.NeedsFirstAdministratorAsync());

        var created = await setup.CreateFirstAdministratorAsync(Owner);

        Assert.True(created.IsSuccess, string.Join("; ", created.Errors.Select(error => error.Message)));
        Assert.False(await setup.NeedsFirstAdministratorAsync());

        var again = await setup.CreateFirstAdministratorAsync(Editor);
        Assert.Equal(UserErrorCodes.AlreadySetUp, Assert.Single(again.Errors).Code);

        var owner = Assert.Single(await directory.ListAsync());
        Assert.Equal(created.Value, owner.Id);
        Assert.Equal(Owner.Email, owner.Email);
        Assert.Contains(FluentCmsRoles.Administrator, owner.Roles);
        Assert.False(owner.IsDisabled);
    }

    [Fact]
    public async Task Users_can_be_created_listed_disabled_and_enabled_again()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Administrator);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var owner = await directory.CreateAsync(Owner);
        var editor = await directory.CreateAsync(Editor);
        Assert.True(owner.IsSuccess);
        Assert.True(editor.IsSuccess);

        Assert.True((await directory.DisableAsync(editor.Value)).IsSuccess);

        var users = await directory.ListAsync();
        Assert.Equal([Editor.Email, Owner.Email], users.Select(user => user.Email));
        Assert.True(users.Single(user => user.Id == editor.Value).IsDisabled);
        Assert.False(users.Single(user => user.Id == owner.Value).IsDisabled);
        Assert.All(users, user => Assert.Contains(FluentCmsRoles.Administrator, user.Roles));

        Assert.True((await directory.EnableAsync(editor.Value)).IsSuccess);
        Assert.False((await directory.ListAsync()).Single(user => user.Id == editor.Value).IsDisabled);

        Assert.Equal(UserErrorCodes.UserNotFound, Assert.Single((await directory.DisableAsync(Guid.NewGuid())).Errors).Code);
        Assert.Equal(UserErrorCodes.UserNotFound, Assert.Single((await directory.EnableAsync(Guid.NewGuid())).Errors).Code);
    }

    [Fact]
    public async Task Every_change_appends_an_event_naming_the_administrator_in_its_unit_of_work()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Administrator);
        await using (var scope = services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
            var editor = await directory.CreateAsync(Editor);
            await directory.DisableAsync(editor.Value);
            await directory.EnableAsync(editor.Value);
            await directory.CreateAsync(Editor with { Password = "short" });
        }

        await using var read = services.CreateAsyncScope();
        var editorId = (await read.ServiceProvider.GetRequiredService<IUserDirectory>().ListAsync()).Single().Id;
        var rows = (await read.ServiceProvider.GetRequiredService<FluentCmsDbContext>().AuditEvents.ToListAsync())
            .OrderBy(row => row.Timestamp)
            .ToList();

        Assert.Equal(["user.created", "user.disabled", "user.enabled"], rows.Select(row => row.Operation));
        Assert.All(rows, row =>
        {
            Assert.Equal(InitiatorKinds.User, row.InitiatorKind);
            Assert.Equal(Administrator.UserId, row.InitiatorId);
            Assert.Equal("user", row.SubjectType);
            Assert.Equal(editorId, row.SubjectId);
        });
    }

    [Fact]
    public async Task Disabling_changes_the_security_stamp_and_nobody_can_disable_themselves()
    {
        using var database = new TemporarySqliteDatabase();
        Guid ownerId;
        Guid editorId;
        await using (var services = await BuildAsync(database, Administrator))
        await using (var scope = services.CreateAsyncScope())
        {
            var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
            ownerId = (await directory.CreateAsync(Owner)).Value;
            editorId = (await directory.CreateAsync(Editor)).Value;
        }

        await using var asOwner = await BuildAsync(database, new UserInitiator(ownerId, "Owner"));
        await using var ownerScope = asOwner.CreateAsyncScope();
        var users = ownerScope.ServiceProvider.GetRequiredService<UserManager<FluentCmsUser>>();
        var ownerDirectory = ownerScope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var stampBefore = (await users.FindByIdAsync(editorId.ToString()))!.SecurityStamp;

        var refused = await ownerDirectory.DisableAsync(ownerId);
        var disabled = await ownerDirectory.DisableAsync(editorId);

        Assert.Equal(UserErrorCodes.CannotDisableSelf, Assert.Single(refused.Errors).Code);
        Assert.True(disabled.IsSuccess);
        Assert.NotEqual(stampBefore, (await users.FindByIdAsync(editorId.ToString()))!.SecurityStamp);
        Assert.False((await ownerDirectory.ListAsync()).Single(user => user.Id == ownerId).IsDisabled);
    }

    [Fact]
    public async Task A_lockout_after_failed_sign_ins_is_not_a_disabled_account()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Administrator);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<FluentCmsUser>>();
        var editor = await users.FindByIdAsync((await directory.CreateAsync(Editor)).Value.ToString());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await users.AccessFailedAsync(editor!);
        }

        Assert.True(await users.IsLockedOutAsync(editor!));
        Assert.False(Assert.Single(await directory.ListAsync()).IsDisabled);
    }

    [Fact]
    public async Task A_weak_password_is_rejected_with_identity_error_codes()
    {
        using var database = new TemporarySqliteDatabase();
        await using var services = await BuildAsync(database, Administrator);
        await using var scope = services.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

        var result = await directory.CreateAsync(Owner with { Password = "short" });

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "PasswordTooShort");
        Assert.Empty(await directory.ListAsync());
    }

    private static async Task<ServiceProvider> BuildAsync(TemporarySqliteDatabase database, Initiator? initiator)
    {
        var services = TestServices.Build(database.Configuration, collection =>
            collection.AddScoped<ICurrentInitiator>(_ => new TestInitiator(initiator)));

        await TestServices.MigrateAsync(services);
        return services;
    }
}
