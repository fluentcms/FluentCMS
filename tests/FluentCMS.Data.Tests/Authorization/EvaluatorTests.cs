using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Query;
using FluentCMS.Data.Tests.Sqlite;
using FluentCMS.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

public sealed class EvaluatorTests : IDisposable
{
    private readonly AuthorizationDatabase _database = new(cms => cms.Authorization
        .AddRowRule<Regional>("region", ctx => article => article.Region == ctx.Principal.Claim("region")));

    private static SchemaType Article => AuthorizationDatabase.Article;

    private static AccessTarget InSiteA => AuthorizationDatabase.InSiteA;

    private QueryDatabase Rows => _database.Database;

    public void Dispose() => _database.Dispose();

    private static string[] Titles(ContentQueryResult result) => [.. result.Items.Select(item => (string)item["title"]!).Order()];

    private static Principal Manager(params Guid[] roles) => AuthorizationDatabase.User(roles, [TokenScopes.Management]);

    [Fact]
    public async Task AuthorSeesOnlyOwnEntriesInARealQuery()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", createdBy: ann);
        Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7());
        var author = AuthorizationDatabase.User([BuiltInRoles.Author], id: ann);

        var result = await _database.QueryAsync(author, "locale=en&withTotal=true");

        Titles(result).ShouldBe(["Mine"]);
        result.Total.ShouldBe(1);
    }

    [Fact]
    public async Task AuthorIsRefusedAnotherEntryByIdAndGetsTheirOwn()
    {
        var ann = Guid.CreateVersion7();
        var mine = Rows.AddArticle("Mine", createdBy: ann);
        var theirs = Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7());
        var author = AuthorizationDatabase.User([BuiltInRoles.Author], [TokenScopes.Management], ann);

        (await _database.CanAccessEntryAsync(author, ContentOperation.Read, mine)).ShouldBeTrue();
        (await _database.CanAccessEntryAsync(author, ContentOperation.Read, theirs)).ShouldBeFalse();
        (await _database.QueryAsync(author, $"locale=en&filter[id][eq]={theirs}")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AuthorChangesOnlyOwnEntriesAndAWriteReadsTheRuleOnTheDraft()
    {
        var ann = Guid.CreateVersion7();
        var mine = Rows.AddArticle("Mine", createdBy: ann, state: ContentState.Draft);
        var theirs = Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7(), state: ContentState.Draft);
        var author = AuthorizationDatabase.User([BuiltInRoles.Author], [TokenScopes.Management], ann);

        (await _database.CanAccessEntryAsync(author, ContentOperation.Update, mine)).ShouldBeTrue();
        (await _database.CanAccessEntryAsync(author, ContentOperation.Delete, mine)).ShouldBeTrue();
        (await _database.CanAccessEntryAsync(author, ContentOperation.Update, theirs)).ShouldBeFalse();
        (await _database.CanAccessEntryAsync(author, ContentOperation.Delete, theirs)).ShouldBeFalse();
        (await _database.CanAccessEntryAsync(author, ContentOperation.Publish, mine)).ShouldBeFalse();
    }

    [Fact]
    public async Task EditorReadsEveryEntry()
    {
        var theirs = Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7());
        var editor = AuthorizationDatabase.User([BuiltInRoles.Editor]);

        Titles(await _database.QueryAsync(editor, "locale=en")).ShouldBe(["Theirs"]);
        (await _database.CanAccessEntryAsync(editor, ContentOperation.Read, theirs)).ShouldBeTrue();
    }

    [Fact]
    public async Task ScopesNarrowWhatARoleGrants()
    {
        var target = InSiteA;

        // No scope claim is no scopes: a token that lost them can read published content and no more.
        var bare = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor]));
        bare.Allows(Article, ContentOperation.Read, target).ShouldBeTrue();
        bare.Allows(Article, ContentOperation.Update, target).ShouldBeFalse();
        bare.Allows(CorePermissions.SchemaRead, target).ShouldBeFalse();
        bare.CanReadDraft(Article, target).ShouldBeFalse();
        bare.FieldsOf(Article, ContentOperation.Update, target).ShouldBeEmpty();
        bare.RowRuleOf(Article, ContentOperation.Update, target).ShouldBe(RowRule.NoRows);

        var draftOnly = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Draft]));
        draftOnly.CanReadDraft(Article, target).ShouldBeTrue();
        draftOnly.Allows(Article, ContentOperation.Update, target).ShouldBeFalse();

        var delivery = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Delivery]));
        delivery.Allows(Article, ContentOperation.Read, target).ShouldBeTrue();
        delivery.Allows(Article, ContentOperation.Publish, target).ShouldBeFalse();
        delivery.CanReadDraft(Article, target).ShouldBeFalse();

        var management = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management]));
        management.Allows(Article, ContentOperation.Update, target).ShouldBeTrue();
        management.CanReadDraft(Article, target).ShouldBeFalse();
    }

    [Fact]
    public async Task ScopesNeverWidenWhatARoleGrants()
    {
        string[] every = [TokenScopes.Management, TokenScopes.Draft, TokenScopes.Mcp, TokenScopes.Preview, TokenScopes.Delivery];

        var author = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Author], every));
        author.Allows(Article, ContentOperation.Publish, InSiteA).ShouldBeFalse();
        author.Allows(Article, ContentOperation.Purge, InSiteA).ShouldBeFalse();
        author.Allows(CorePermissions.UsersManage, InSiteA).ShouldBeFalse();

        var member = await _database.PermissionsAsync(AuthorizationDatabase.User([BuiltInRoles.Member], every));
        member.Allows(Article, ContentOperation.Read, InSiteA).ShouldBeFalse();
        member.CanReadDraft(Article, InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task TheMcpScopeManagesOnlyThroughMcp()
    {
        var principal = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Mcp]);

        (await _database.PermissionsAsync(principal, Origin.Mcp)).Allows(Article, ContentOperation.Update, InSiteA).ShouldBeTrue();
        (await _database.PermissionsAsync(principal, Origin.Api)).Allows(Article, ContentOperation.Update, InSiteA).ShouldBeFalse();
    }

    [Theory]
    [InlineData("*", "content.article.read", true)]
    [InlineData("*", "users.manage", true)]
    [InlineData("content.*.read", "content.article.read", true)]
    [InlineData("content.*.read", "content.author.read", true)]
    [InlineData("content.*.read", "content.article.update", false)]
    [InlineData("content.article.*", "content.article.purge", true)]
    [InlineData("content.article.*", "content.author.read", false)]
    [InlineData("content.article.read", "content.article.read", true)]
    [InlineData("content.article.read", "content.article.update", false)]
    public async Task AWildcardGrantMatchesByWholeSegments(string grant, string permission, bool allowed)
    {
        var role = _database.AddRole("Wild", null, (grant, null));

        (await _database.PermissionsAsync(Manager(role))).Allows(permission, InSiteA).ShouldBe(allowed);
    }

    [Fact]
    public async Task SitesAndLocalesLimitAGrant()
    {
        var role = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteA], Locales = ["en"] }));
        var permissions = await _database.PermissionsAsync(Manager(role));

        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteA, "EN")).ShouldBeTrue();
        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteB, "en")).ShouldBeFalse();
        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteA, "fr")).ShouldBeFalse();
        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(null, "en")).ShouldBeFalse();
        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteA, "")).ShouldBeFalse();
    }

    [Fact]
    public async Task AQueryIsAskedAboutItsOwnSiteAndLocale()
    {
        Rows.AddArticle("In A");
        Rows.AddArticle("In B", site: QueryDatabase.SiteB);
        var role = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteA], Locales = ["en"] }));
        var principal = Manager(role);

        Titles(await _database.QueryAsync(principal, "locale=en")).ShouldBe(["In A"]);

        var otherSite = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(principal, "locale=en", site: AuthorizationDatabase.SiteB));
        otherSite.Errors.Single().Path.ShouldBe("type");

        var otherLocale = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(principal, "locale=fr"));
        otherLocale.Errors.Single().Path.ShouldBe("type");
    }

    [Fact]
    public async Task AQueryOfEverySiteNeedsAGrantWithNoSiteLimit()
    {
        Rows.AddArticle("In A");
        Rows.AddArticle("In B", site: QueryDatabase.SiteB);
        var limited = _database.AddRole("Limited", null, ("content.article.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteA] }));

        using var scope = Rows.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = AuthorizationDatabase.Operation(Manager(limited));
        var engine = scope.ServiceProvider.GetRequiredService<ContentQueryEngine>();

        await Should.ThrowAsync<ValidationException>(() => engine.ExecuteAsync("article", BracketQueryParser.Parse("locale=en"), TestContext.Current.CancellationToken));

        accessor.Current = null;
    }

    [Fact]
    public async Task AGlobalTypeIsAskedAboutTheSiteOfTheOperation()
    {
        Rows.AddTag("shared");
        var limited = _database.AddRole("Limited", null, ("content.tag.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteA] }));

        var result = await _database.QueryAsync(Manager(limited), "", type: "tag");

        result.Items.Single()["name"].ShouldBe("shared");
    }

    [Fact]
    public async Task ARoleOfOneSiteGrantsNothingInAnother()
    {
        var role = _database.AddRole("Site editor", QueryDatabase.SiteA, ("content.article.read", null));
        var permissions = await _database.PermissionsAsync(Manager(role));

        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteA, "en")).ShouldBeTrue();
        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteB, "en")).ShouldBeFalse();
    }

    [Fact]
    public async Task TwoGrantsOfOnePermissionUnionTheirConstraints()
    {
        var role = _database.AddRole(
            "Two",
            null,
            ("content.article.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteA], Fields = ["title"] }),
            ("content.article.read", new PermissionConstraints { Sites = [AuthorizationDatabase.SiteB], Fields = ["slug"] }),
            ("content.article.read", new PermissionConstraints { Locales = ["fr"], Fields = ["views"] }));
        var permissions = await _database.PermissionsAsync(Manager(role));

        permissions.Allows(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteB, "en")).ShouldBeTrue();
        permissions.FieldsOf(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteA, "en")).ShouldBe(["title"], ignoreOrder: true);
        permissions.FieldsOf(Article, ContentOperation.Read, new AccessTarget(AuthorizationDatabase.SiteB, "fr")).ShouldBe(["slug", "views"], ignoreOrder: true);
    }

    [Fact]
    public async Task TwoGrantsWithDifferentRowRulesAreAUnion()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", createdBy: ann, values: new() { ["region"] = "us" });
        Rows.AddArticle("Regional", createdBy: Guid.CreateVersion7(), values: new() { ["region"] = "eu" });
        Rows.AddArticle("Neither", createdBy: Guid.CreateVersion7(), values: new() { ["region"] = "us" });
        var role = _database.AddRole(
            "Both",
            null,
            ("content.article.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule }),
            ("content.article.read", new PermissionConstraints { RowRule = "region" }));
        var principal = AuthorizationDatabase.User([role], null, ann, new() { ["region"] = "eu" });

        Titles(await _database.QueryAsync(principal, "locale=en")).ShouldBe(["Mine", "Regional"]);
    }

    [Fact]
    public async Task TwoRolesUnionAndAGrantWithNoRuleMeansEveryRow()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", createdBy: ann);
        Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7());

        var both = AuthorizationDatabase.User([BuiltInRoles.Author, BuiltInRoles.Editor], id: ann);

        Titles(await _database.QueryAsync(both, "locale=en")).ShouldBe(["Mine", "Theirs"]);
    }

    [Fact]
    public async Task TheRegionRuleComparesWithAClaim()
    {
        Rows.AddArticle("Europe", new() { ["region"] = "eu" });
        Rows.AddArticle("Elsewhere", new() { ["region"] = "us" });
        Rows.AddArticle("Nowhere");
        var role = _database.AddRole("Regional", null, ("content.article.read", new PermissionConstraints { RowRule = "region" }));

        Titles(await _database.QueryAsync(AuthorizationDatabase.User([role], claims: new() { ["region"] = "eu" }), "locale=en")).ShouldBe(["Europe"]);
    }

    [Fact]
    public async Task AMissingClaimMatchesNoRowInsteadOfRowsWithNoValue()
    {
        Rows.AddArticle("Nowhere");
        var role = _database.AddRole("Regional", null, ("content.article.read", new PermissionConstraints { RowRule = "region" }));

        (await _database.QueryAsync(AuthorizationDatabase.User([role]), "locale=en")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ARowRuleThisVersionDoesNotHaveAddsNoRows()
    {
        Rows.AddArticle("Any");
        var role = _database.AddRole("Unknown", null, ("content.article.read", new PermissionConstraints { RowRule = "from-the-future" }));

        (await _database.QueryAsync(AuthorizationDatabase.User([role]), "locale=en")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task AConstraintThisVersionDoesNotKnowFailsInsteadOfWidening()
    {
        var role = _database.AddRole("Unknown");
        using (var scope = Rows.NewScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Add(new RoleGrant { RoleId = role, Permission = "content.article.read", Constraints = """{"tenants":["a"]}""" });
            context.SaveChanges();
        }

        await Should.ThrowAsync<System.Text.Json.JsonException>(() => _database.PermissionsAsync(Manager(role)));
    }

    [Fact]
    public async Task ARoleChangeInvalidatesTheCacheThroughTheStamp()
    {
        var role = _database.AddRole("Changing", null, ("content.article.read", null));
        var principal = Manager(role);
        (await _database.PermissionsAsync(principal)).Allows(Article, ContentOperation.Update, InSiteA).ShouldBeFalse();

        _database.Grant(role, "content.article.update");

        (await _database.PermissionsAsync(principal)).Allows(Article, ContentOperation.Update, InSiteA).ShouldBeTrue();
    }

    [Fact]
    public async Task ABulkChangeIsNotSeenUntilItRaisesTheStamp()
    {
        var role = _database.AddRole("Bulk", null, ("content.article.read", null));
        var principal = Manager(role);
        (await _database.PermissionsAsync(principal)).Allows(Article, ContentOperation.Read, InSiteA).ShouldBeTrue();

        using var scope = Rows.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Set<RoleGrant>().Where(grant => grant.RoleId == role).ExecuteUpdate(grant => grant.SetProperty(item => item.Permission, "content.tag.read"));

        (await _database.PermissionsAsync(principal)).Allows(Article, ContentOperation.Read, InSiteA).ShouldBeTrue();

        context.Set<Role>().Where(item => item.Id == role).ExecuteUpdate(item => item.SetProperty(row => row.PermissionsVersion, row => row.PermissionsVersion + 1));

        (await _database.PermissionsAsync(principal)).Allows(Article, ContentOperation.Read, InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task ARequestAsksOnceAndKeepsItsAnswer()
    {
        var role = _database.AddRole("Stable", null, ("content.article.read", null));
        var operation = AuthorizationDatabase.Operation(Manager(role));

        using var scope = Rows.NewScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>();
        var first = await evaluator.ResolveAsync(operation, TestContext.Current.CancellationToken);

        _database.Grant(role, "content.article.update");

        (await evaluator.ResolveAsync(operation, TestContext.Current.CancellationToken)).ShouldBeSameAs(first);
    }

    [Fact]
    public async Task TwoPrincipalsWithOneIdAndDifferentScopesDoNotShareAnAnswer()
    {
        var id = Guid.CreateVersion7();

        using var scope = Rows.NewScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IAuthorizationEvaluator>();
        var wide = await evaluator.ResolveAsync(AuthorizationDatabase.Operation(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management], id)), TestContext.Current.CancellationToken);
        var narrow = await evaluator.ResolveAsync(AuthorizationDatabase.Operation(AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Delivery], id)), TestContext.Current.CancellationToken);

        wide.Allows(Article, ContentOperation.Update, InSiteA).ShouldBeTrue();
        narrow.Allows(Article, ContentOperation.Update, InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task PublicIsClosedUntilThePublicRoleGrantsIt()
    {
        Rows.AddArticle("Open");

        var closed = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Principal.Anonymous, "locale=en"));
        closed.Errors.Single().Path.ShouldBe("type");

        _database.Grant(BuiltInRoles.Public, "content.article.read");

        Titles(await _database.QueryAsync(Principal.Anonymous, "locale=en")).ShouldBe(["Open"]);
        (await _database.PermissionsAsync(Principal.Anonymous)).Allows(Article, ContentOperation.Update, InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task OnlyTheAnonymousPrincipalGetsThePublicRole()
    {
        _database.Grant(BuiltInRoles.Public, "content.article.read");

        (await _database.PermissionsAsync(AuthorizationDatabase.User([]))).Allows(Article, ContentOperation.Read, InSiteA).ShouldBeFalse();
    }

    [Fact]
    public async Task ASitesOwnPublicRoleWinsAndOtherSitesFallBack()
    {
        _database.Grant(BuiltInRoles.Public, "content.tag.read");
        _database.AddRole(BuiltInRoles.PublicName, QueryDatabase.SiteA, ("content.article.read", null));

        var inA = await _database.PermissionsAsync(Principal.Anonymous, site: AuthorizationDatabase.SiteA);
        inA.Allows("content.article.read", new AccessTarget(AuthorizationDatabase.SiteA, "en")).ShouldBeTrue();
        inA.Allows("content.tag.read", new AccessTarget(AuthorizationDatabase.SiteA, "en")).ShouldBeFalse();

        var inB = await _database.PermissionsAsync(Principal.Anonymous, site: AuthorizationDatabase.SiteB);
        inB.Allows("content.article.read", new AccessTarget(AuthorizationDatabase.SiteB, "en")).ShouldBeFalse();
        inB.Allows("content.tag.read", new AccessTarget(AuthorizationDatabase.SiteB, "en")).ShouldBeTrue();
    }

    [Fact]
    public async Task AnOwnEntriesRuleOnThePublicRoleDoesNotOpenWhatTheSystemWrote()
    {
        Rows.AddArticle("Written by the system", createdBy: Principal.System.Id);
        _database.Grant(BuiltInRoles.Public, "content.article.read", new PermissionConstraints { RowRule = BuiltInRoles.OwnEntriesRule });

        (await _database.QueryAsync(Principal.Anonymous, "locale=en")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task DraftsAreClosedWithoutTheScopeAndOpenWithIt()
    {
        var entry = Rows.AddArticle("Draft", state: ContentState.Draft);

        var editor = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management]);
        var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(editor, "locale=en&status=draft"));
        refused.Errors.Single().Code.ShouldBe(QueryErrorCodes.DraftNotAllowed);
        (await _database.CanAccessEntryAsync(editor, ContentOperation.Read, entry, QueryStatus.Draft)).ShouldBeFalse();

        var withDraft = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Draft]);
        Titles(await _database.QueryAsync(withDraft, "locale=en&status=draft")).ShouldBe(["Draft"]);
        (await _database.CanAccessEntryAsync(withDraft, ContentOperation.Read, entry, QueryStatus.Draft)).ShouldBeTrue();

        var previewKey = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Preview]);
        Titles(await _database.QueryAsync(previewKey, "locale=en&status=draft")).ShouldBe(["Draft"]);
    }

    [Fact]
    public async Task TheDraftScopeDoesNotOpenATypeTheRolesDoNot()
    {
        Rows.AddArticle("Draft", state: ContentState.Draft);

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(AuthorizationDatabase.User([BuiltInRoles.Member], [TokenScopes.Draft]), "locale=en&status=draft"));

        refused.Errors.Single().Path.ShouldBe("type");
    }

    [Fact]
    public async Task AnonymousNeverReadsDrafts()
    {
        _database.Grant(BuiltInRoles.Public, "content.article.read");
        Rows.AddArticle("Draft", state: ContentState.Draft);

        var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(Principal.Anonymous, "locale=en&status=draft"));

        refused.Errors.Single().Code.ShouldBe(QueryErrorCodes.DraftNotAllowed);
    }

    [Fact]
    public async Task TheRuleGoesOnADraftQueryToo()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", state: ContentState.Draft, createdBy: ann);
        Rows.AddArticle("Theirs", state: ContentState.Draft, createdBy: Guid.CreateVersion7());

        var author = AuthorizationDatabase.User([BuiltInRoles.Author], [TokenScopes.Draft], ann);

        Titles(await _database.QueryAsync(author, "locale=en&status=draft")).ShouldBe(["Mine"]);
    }

    [Fact]
    public async Task ADeliveryPrincipalCannotReadOrFilterByTheAuthorFieldsButTheRuleStillWorks()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", createdBy: ann);
        Rows.AddArticle("Theirs", createdBy: Guid.CreateVersion7());
        var delivery = AuthorizationDatabase.User([BuiltInRoles.Author], [TokenScopes.Delivery], ann);

        Titles(await _database.QueryAsync(delivery, "locale=en")).ShouldBe(["Mine"]);

        foreach (var (query, path) in new[]
        {
            ($"locale=en&filter[createdBy][eq]={ann}", "filter"),
            ($"locale=en&filter[updatedBy][eq]={ann}", "filter"),
            ("locale=en&fields=createdBy", "fields"),
            ("locale=en&fields=updatedBy", "fields"),
        })
        {
            var refused = await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(delivery, query));
            refused.Errors.Single().Code.ShouldBe(QueryErrorCodes.UnknownField, path);
        }
    }

    [Fact]
    public async Task AManagementPrincipalCanReadTheAuthorFields()
    {
        var ann = Guid.CreateVersion7();
        Rows.AddArticle("Mine", createdBy: ann);
        var manager = AuthorizationDatabase.User([BuiltInRoles.Editor], [TokenScopes.Management]);

        var entry = (await _database.QueryAsync(manager, $"locale=en&filter[createdBy][eq]={ann}&fields=title,createdBy")).Items.Single();

        entry["createdBy"].ShouldBe(ann);
    }

    [Fact]
    public async Task ARowRuleMayNameAFieldThePrincipalCannotRead()
    {
        Rows.AddArticle("Europe", new() { ["region"] = "eu" });
        Rows.AddArticle("Elsewhere", new() { ["region"] = "us" });
        var role = _database.AddRole("Regional", null, ("content.article.read", new PermissionConstraints { RowRule = "region", Fields = ["title"] }));
        var principal = AuthorizationDatabase.User([role], claims: new() { ["region"] = "eu" });

        var entry = (await _database.QueryAsync(principal, "locale=en")).Items.Single();

        entry["title"].ShouldBe("Europe");
        entry.ContainsKey("region").ShouldBeFalse();
        (await Should.ThrowAsync<ValidationException>(() => _database.QueryAsync(principal, "locale=en&filter[region][eq]=eu"))).Errors.Single().Code.ShouldBe(QueryErrorCodes.UnknownField);
    }

    [Fact]
    public async Task OutsideAnOperationEverythingIsClosed()
    {
        Rows.AddArticle("Any");
        using var scope = Rows.NewScope();
        var engine = scope.ServiceProvider.GetRequiredService<ContentQueryEngine>();

        await Should.ThrowAsync<ValidationException>(() => engine.ExecuteAsync("article", BracketQueryParser.Parse("locale=en"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ARuleThatDoesNotFitTheSchemaStopsTheHost()
    {
        using var database = new SqliteFileDatabase();
        using var services = database.CreateServices(QueryDatabase.Schema, cms => cms.Authorization
            .AddRowRule<Regional>("bad", ctx => article => article.Missing == "x"));

        Should.Throw<FluentCmsConfigurationException>(() => services.GetServices<IHostedService>().ToList())
            .Code.ShouldBe(AuthorizationErrorCodes.InvalidRowRule);
    }

    [FluentCMS.Schema.Collection("article")]
    private sealed class Regional : ContentItem
    {
        public string? Region { get; set; }

        public string? Missing { get; set; }
    }
}
