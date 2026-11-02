using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

public sealed class PermissionMatrixTests : IDisposable
{
    private static readonly string[] AllScopes = [TokenScopes.Management, TokenScopes.Draft];

    private readonly AuthorizationDatabase _database = new();

    public void Dispose() => _database.Dispose();

    // Role, operation, allowed, row rule on the grant.
    public static TheoryData<string, ContentOperation, bool, string?> Matrix => new()
    {
        { "SuperAdmin", ContentOperation.Create, true, null },
        { "SuperAdmin", ContentOperation.Read, true, null },
        { "SuperAdmin", ContentOperation.Update, true, null },
        { "SuperAdmin", ContentOperation.Delete, true, null },
        { "SuperAdmin", ContentOperation.Publish, true, null },
        { "SuperAdmin", ContentOperation.Unpublish, true, null },
        { "SuperAdmin", ContentOperation.Restore, true, null },
        { "SuperAdmin", ContentOperation.Purge, true, null },

        { "Editor", ContentOperation.Create, true, null },
        { "Editor", ContentOperation.Read, true, null },
        { "Editor", ContentOperation.Update, true, null },
        { "Editor", ContentOperation.Delete, true, null },
        { "Editor", ContentOperation.Publish, true, null },
        { "Editor", ContentOperation.Unpublish, true, null },
        { "Editor", ContentOperation.Restore, true, null },
        { "Editor", ContentOperation.Purge, false, null },

        { "Author", ContentOperation.Create, true, null },
        { "Author", ContentOperation.Read, true, BuiltInRoles.OwnEntriesRule },
        { "Author", ContentOperation.Update, true, BuiltInRoles.OwnEntriesRule },
        { "Author", ContentOperation.Delete, true, BuiltInRoles.OwnEntriesRule },
        { "Author", ContentOperation.Publish, false, null },
        { "Author", ContentOperation.Unpublish, false, null },
        { "Author", ContentOperation.Restore, false, null },
        { "Author", ContentOperation.Purge, false, null },

        { "Public", ContentOperation.Create, false, null },
        { "Public", ContentOperation.Read, false, null },
        { "Public", ContentOperation.Update, false, null },
        { "Public", ContentOperation.Delete, false, null },
        { "Public", ContentOperation.Publish, false, null },
        { "Public", ContentOperation.Unpublish, false, null },
        { "Public", ContentOperation.Restore, false, null },
        { "Public", ContentOperation.Purge, false, null },

        { "Member", ContentOperation.Create, false, null },
        { "Member", ContentOperation.Read, false, null },
        { "Member", ContentOperation.Update, false, null },
        { "Member", ContentOperation.Delete, false, null },
        { "Member", ContentOperation.Publish, false, null },
        { "Member", ContentOperation.Unpublish, false, null },
        { "Member", ContentOperation.Restore, false, null },
        { "Member", ContentOperation.Purge, false, null },
    };

    private static Guid Role(string name) => name switch
    {
        "SuperAdmin" => BuiltInRoles.SuperAdmin,
        "Editor" => BuiltInRoles.Editor,
        "Author" => BuiltInRoles.Author,
        "Public" => BuiltInRoles.Public,
        "Member" => BuiltInRoles.Member,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task EveryRoleAndOperation(string role, ContentOperation operation, bool allowed, string? rule)
    {
        var id = Guid.CreateVersion7();
        var permissions = await _database.PermissionsAsync(AuthorizationDatabase.User([Role(role)], AllScopes, id));

        permissions.Allows(AuthorizationDatabase.Article, operation, AuthorizationDatabase.InSiteA).ShouldBe(allowed);

        var filter = permissions.RowRuleOf(AuthorizationDatabase.Article, operation, AuthorizationDatabase.InSiteA);
        if (!allowed)
        {
            filter.ShouldBe(RowRule.NoRows);
        }
        else if (rule is null)
        {
            filter.ShouldBeNull();
        }
        else
        {
            filter.ShouldBe(new FieldFilter("createdBy", FilterOperator.Equal, id));
        }
    }

    [Theory]
    [InlineData("SuperAdmin", true, true)]
    [InlineData("Editor", true, false)]
    [InlineData("Author", true, false)]
    [InlineData("Public", false, false)]
    [InlineData("Member", false, false)]
    public async Task CorePermissionsOfEveryRole(string role, bool schemaRead, bool usersManage)
    {
        var permissions = await _database.PermissionsAsync(AuthorizationDatabase.User([Role(role)], AllScopes));

        permissions.Allows(CorePermissions.SchemaRead, AuthorizationDatabase.InSiteA).ShouldBe(schemaRead);
        permissions.Allows(CorePermissions.UsersManage, AuthorizationDatabase.InSiteA).ShouldBe(usersManage);
    }

    [Fact]
    public async Task TheSystemPrincipalHoldsEverythingOnEveryRow()
    {
        var permissions = await _database.PermissionsAsync(Principal.System, Origin.Job);

        foreach (var operation in Enum.GetValues<ContentOperation>())
        {
            permissions.Allows(AuthorizationDatabase.Article, operation, AuthorizationDatabase.InSiteA).ShouldBeTrue();
            permissions.RowRuleOf(AuthorizationDatabase.Article, operation, AuthorizationDatabase.InSiteA).ShouldBeNull();
        }

        permissions.CanReadDraft(AuthorizationDatabase.Article, AuthorizationDatabase.InSiteA).ShouldBeTrue();
        permissions.CanReadField(AuthorizationDatabase.Article, "createdBy", AuthorizationDatabase.InSiteA).ShouldBeTrue();
    }

    [Fact]
    public async Task APrincipalWithNoRolesHoldsNothing()
    {
        var permissions = await _database.PermissionsAsync(AuthorizationDatabase.User([], AllScopes));

        permissions.Allows(AuthorizationDatabase.Article, ContentOperation.Read, AuthorizationDatabase.InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task ARoleIdThatDoesNotParseOrExistGrantsNothing()
    {
        var principal = new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", new Dictionary<string, string>
        {
            [ClaimPrincipalRoles.Claim] = $"not-a-guid {Guid.CreateVersion7()}",
            [TokenScopes.Claim] = TokenScopes.Management,
        });

        var permissions = await _database.PermissionsAsync(principal);

        permissions.Allows(AuthorizationDatabase.Article, ContentOperation.Read, AuthorizationDatabase.InSiteA).ShouldBeFalse();
    }
}
