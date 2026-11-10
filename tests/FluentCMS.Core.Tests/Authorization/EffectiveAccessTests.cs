using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Schema;
using FluentCMS.Providers;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

// What core tells a surface that lists only what a call would allow, such as the MCP tool list (spec 5.4).
public class EffectiveAccessTests
{
    private static readonly SiteId Site = new(Guid.CreateVersion7());

    private static readonly SchemaModel Model = new SchemaBuilder()
        .AddCollection("article", type => type.Field("title", "text").Field("body", "text").Field("note", "text"))
        .AddCollection("secret", type => type.Field("title", "text"))
        .Build();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATypeIsDescribedOnlyWhenThePrincipalMayReadItWithTheOperationsItsGrantsGive()
    {
        var access = Access(
            Managing(),
            new EffectiveGrant("content.article.read", PermissionConstraints.None, null),
            new EffectiveGrant("content.article.create", PermissionConstraints.None, null),
            new EffectiveGrant("content.secret.update", PermissionConstraints.None, null));

        var content = await access.ContentAsync(Token);

        content.Schema.ContentTypes.Select(type => type.Name).ShouldBe(["article"]);
        var article = content.Types.ShouldHaveSingleItem().Value;
        article.ShouldSatisfyAllConditions(
            may => may.Drafts.ShouldBeTrue(),
            may => may.Create.ShouldBeTrue(),
            may => may.Update.ShouldBeFalse(),
            may => may.Delete.ShouldBeFalse(),
            may => may.Publish.ShouldBeFalse(),
            may => may.Unpublish.ShouldBeFalse(),
            may => may.CreatableFields.ShouldBe(["title", "body", "note"], ignoreOrder: true),
            may => may.UpdatableFields.ShouldBeEmpty());
    }

    [Fact]
    public async Task TheWritableFieldsAreTheReadableOnesThatSomeGrantOfTheOperationLists()
    {
        var access = Access(
            Managing(),
            new EffectiveGrant("content.article.read", new PermissionConstraints { Fields = ["title", "body"] }, null),
            new EffectiveGrant("content.article.update", new PermissionConstraints { Fields = ["title"] }, null),
            new EffectiveGrant("content.article.update", new PermissionConstraints { Fields = ["note"], RowRule = "own-entries" }, null),
            new EffectiveGrant("content.article.create", new PermissionConstraints { RowRule = "own-entries" }, null));

        var content = await access.ContentAsync(Token);

        content.Schema.FindContentType("article")!.Fields.Select(field => field.Name).ShouldBe(["title", "body"]);
        var article = content.Types["article"];
        article.Update.ShouldBeTrue();
        article.UpdatableFields.ShouldBe(["title"]);
        article.CreatableFields.ShouldBe(["title", "body"], ignoreOrder: true);
    }

    // A Delivery key reads what is published and manages nothing, whatever its role grants (spec 4.8).
    [Fact]
    public async Task APrincipalThatCannotManageReadsWhatIsPublishedAndWritesNothing()
    {
        var delivery = new Principal(PrincipalKind.ApiKey, Guid.CreateVersion7(), "key", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Delivery });
        var access = Access(delivery, new EffectiveGrant("*", PermissionConstraints.None, null));

        var content = await access.ContentAsync(Token);
        var held = await access.HeldAsync(["media.read", "media.upload"], Token);

        content.Types.Keys.ShouldBe(["article", "secret"], ignoreOrder: true);
        content.Types.Values.ShouldAllBe(may => !may.Drafts && !may.Create && !may.Update && !may.Delete && !may.Publish && !may.Unpublish);
        held.ShouldBeEmpty();
    }

    [Fact]
    public async Task APermissionIsHeldThroughAGrantWithOrWithoutAResourceLimit()
    {
        var access = Access(
            Managing(),
            new EffectiveGrant("media.read", PermissionConstraints.None, null),
            new EffectiveGrant("media.upload", new PermissionConstraints { Resources = ["folder"] }, null),
            new EffectiveGrant("media.update", new PermissionConstraints { Sites = [new SiteId(Guid.CreateVersion7())] }, null));

        var held = await access.HeldAsync(["media.read", "media.upload", "media.update", "media.delete"], Token);

        held.ShouldBe(["media.read", "media.upload"], ignoreOrder: true);
    }

    // A token of an MCP client that registered itself is held to the role of its client (spec 4.8, "Client roles as
    // built"): the description is what both the principal's grants and that role's give, as a call would be decided.
    [Fact]
    public async Task ThroughAClientWithARoleOnlyWhatBothThePrincipalAndThatRoleAllowIsDescribed()
    {
        var access = Through(
            [new EffectiveGrant("*", PermissionConstraints.None, null)],
            [
                new EffectiveGrant("content.article.read", new PermissionConstraints { Fields = ["title", "body"] }, null),
                new EffectiveGrant("content.article.create", new PermissionConstraints { Fields = ["title"] }, null),
                new EffectiveGrant("content.article.update", PermissionConstraints.None, null),
                new EffectiveGrant("media.read", PermissionConstraints.None, null),
            ]);

        var content = await access.ContentAsync(Token);
        var held = await access.HeldAsync(["media.read", "media.upload"], Token);

        content.Schema.ContentTypes.Select(type => type.Name).ShouldBe(["article"]);
        content.Schema.FindContentType("article")!.Fields.Select(field => field.Name).ShouldBe(["title", "body"]);
        content.Types.ShouldHaveSingleItem().Value.ShouldSatisfyAllConditions(
            may => may.Drafts.ShouldBeTrue(),
            may => may.Create.ShouldBeTrue(),
            may => may.Update.ShouldBeTrue(),
            may => may.Delete.ShouldBeFalse(),
            may => may.Publish.ShouldBeFalse(),
            may => may.Unpublish.ShouldBeFalse(),
            may => may.CreatableFields.ShouldBe(["title"]),
            may => may.UpdatableFields.ShouldBe(["title", "body"], ignoreOrder: true));
        held.ShouldBe(["media.read"]);
    }

    [Fact]
    public async Task TheWritableFieldsThroughAClientAreTheOnesBothFieldListsHold()
    {
        var read = new EffectiveGrant("content.article.read", PermissionConstraints.None, null);
        var access = Through(
            [read, new EffectiveGrant("content.article.update", new PermissionConstraints { Fields = ["title", "body"] }, null), new EffectiveGrant("content.article.create", new PermissionConstraints { Fields = ["title"] }, null)],
            [read, new EffectiveGrant("content.article.update", new PermissionConstraints { Fields = ["body", "note"] }, null), new EffectiveGrant("content.article.create", new PermissionConstraints { Fields = ["note"] }, null)]);

        var article = (await access.ContentAsync(Token)).Types["article"];

        article.Update.ShouldBeTrue();
        article.UpdatableFields.ShouldBe(["body"]);
        article.Create.ShouldBeTrue();
        article.CreatableFields.ShouldBeEmpty();
    }

    // The role of the client only removes: a wide one gives the principal no operation and no field of its own.
    [Fact]
    public async Task AClientWithAWideRoleAddsNothingToWhatThePrincipalMayDo()
    {
        var access = Through(
            [
                new EffectiveGrant("content.article.read", PermissionConstraints.None, null),
                new EffectiveGrant("content.article.update", new PermissionConstraints { Fields = ["title"] }, null),
            ],
            [new EffectiveGrant("*", PermissionConstraints.None, null)]);

        var content = await access.ContentAsync(Token);

        content.Types.Keys.ShouldBe(["article"]);
        var article = content.Types["article"];
        article.Update.ShouldBeTrue();
        article.UpdatableFields.ShouldBe(["title"]);
        article.Create.ShouldBeFalse();
        article.CreatableFields.ShouldBeEmpty();
        article.Publish.ShouldBeFalse();
        (await access.HeldAsync(["media.read"], Token)).ShouldBeEmpty();
    }

    // A client whose role is gone has no grant, which leaves nothing to describe.
    [Fact]
    public async Task AClientRoleWithNoGrantDescribesNothing()
    {
        var access = Through([new EffectiveGrant("*", PermissionConstraints.None, null)], []);

        var content = await access.ContentAsync(Token);

        content.Types.ShouldBeEmpty();
        content.Schema.ContentTypes.ShouldBeEmpty();
        (await access.HeldAsync(["media.read"], Token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task SearchIsOfferedOnlyWhileTheSearchSlotIsFilled()
    {
        var grant = new EffectiveGrant("content.article.read", PermissionConstraints.None, null);

        (await Access(Managing(), new NoSearchIndex(), grant).ContentAsync(Token)).Search.ShouldBeFalse();
        (await Access(Managing(), new SearchIndex(), grant).ContentAsync(Token)).Search.ShouldBeTrue();
    }

    [Fact]
    public async Task TheSystemPrincipalMayDoEverything()
    {
        var accessor = new OperationContextAccessor();
        using var system = SystemOperation.Begin(accessor, Origin.Seed, Site);
        var access = new EffectiveAccess(Model, new Evaluator([]), accessor, new NoSearchIndex());

        var content = await access.ContentAsync(Token);

        content.Schema.ShouldBeSameAs(Model);
        content.Types.Values.ShouldAllBe(may => may.Drafts && may.Create && may.Update && may.Delete && may.Publish && may.Unpublish);
        content.Types["article"].UpdatableFields.ShouldBe(["title", "body", "note"], ignoreOrder: true);
    }

    [Fact]
    public async Task WithoutAnOperationNothingIsDescribed()
    {
        var access = new EffectiveAccess(Model, new Evaluator([]), new OperationContextAccessor(), new NoSearchIndex());

        (await Should.ThrowAsync<InvalidOperationException>(() => access.ContentAsync(Token).AsTask())).Message.ShouldContain(AuthorizationErrorCodes.NoOperation);
        await Should.ThrowAsync<InvalidOperationException>(() => access.HeldAsync(["media.read"], Token).AsTask());
    }

    private static Principal Managing() =>
        new(PrincipalKind.AdminUser, Guid.CreateVersion7(), "agent", new Dictionary<string, string> { [TokenScopes.Claim] = TokenScopes.Management });

    private static EffectiveAccess Access(Principal principal, params EffectiveGrant[] grants) => Access(principal, new NoSearchIndex(), grants);

    private static EffectiveAccess Access(Principal principal, ISearchIndex search, params EffectiveGrant[] grants)
    {
        var accessor = new OperationContextAccessor { Current = new OperationContext(principal, Origin.Mcp, Site, "en", null, "test", DateTimeOffset.UtcNow) };
        return new EffectiveAccess(Model, new Evaluator(grants), accessor, search);
    }

    // The principal with its own grants, through a client whose role has the others.
    private static EffectiveAccess Through(EffectiveGrant[] grants, EffectiveGrant[] clientRole)
    {
        var accessor = new OperationContextAccessor { Current = new OperationContext(Managing(), Origin.Mcp, Site, "en", null, "test", DateTimeOffset.UtcNow) };
        return new EffectiveAccess(Model, new Evaluator(grants, clientRole), accessor, new NoSearchIndex());
    }

    private sealed class SearchIndex : ISearchIndex
    {
        public Task<IReadOnlyList<Guid>> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class Evaluator(IReadOnlyList<EffectiveGrant> grants, IReadOnlyList<EffectiveGrant>? clientRole = null) : IAuthorizationEvaluator
    {
        private static readonly RowRuleRegistry Rules = new(Model, new ContentQueryOptions(), []);

        public ValueTask<EffectivePermissions> ResolveAsync(OperationContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new EffectivePermissions(context, grants, Rules, clientRole is null ? null : new EffectivePermissions(context, clientRole, Rules)));

        public ValueTask<bool> CanAccessEntryAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid entry, AccessTarget target, QueryStatus status = QueryStatus.Published, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WritableFields> WritableFieldsAsync(OperationContext context, SchemaType type, ContentOperation operation, Guid? entry, AccessTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
