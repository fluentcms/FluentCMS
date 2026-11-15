using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

// A version names its author by kind and by the name it had when the version was written, as the activity log names its
// actor (#3097).
public sealed class VersionAuthorTests : IDisposable
{
    private readonly ServiceDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static Principal Of(PrincipalKind kind, string name, Guid? id = null) =>
        new(kind, id ?? Guid.CreateVersion7(), name, new Dictionary<string, string>
        {
            [ClaimPrincipalRoles.Claim] = BuiltInRoles.Editor.ToString(),
            [TokenScopes.Claim] = TokenScopes.Management,
        });

    private Task<IReadOnlyList<VersionInfo>> VersionsAsync(Guid id) =>
        _database.AsAsync(Editor, services => services.GetRequiredService<IContentService>().GetVersionsAsync("article", id, cancellationToken: Token));

    private async Task<(Guid Id, VersionInfo Version)> SavedByAsync(Principal principal)
    {
        var entry = await _database.CreateAsync(principal, new() { ["title"] = $"T {Guid.CreateVersion7()}" });
        return (IdOf(entry), (await VersionsAsync(IdOf(entry))).ShouldHaveSingleItem());
    }

    [Theory]
    [InlineData(PrincipalKind.AdminUser, "Ada Lovelace")]
    [InlineData(PrincipalKind.ApiKey, "Build key")]
    [InlineData(PrincipalKind.ServiceAccount, "Importer")]
    public async Task AVersionNamesTheKindAndTheNameOfItsAuthor(PrincipalKind kind, string name)
    {
        var principal = Of(kind, name);

        var (id, version) = await SavedByAsync(principal);

        version.AuthorId.ShouldBe(principal.Id);
        version.AuthorKind.ShouldBe(kind);
        version.AuthorName.ShouldBe(name);
        var stored = _database.Versions(id).ShouldHaveSingleItem();
        stored.AuthorKind.ShouldBe(kind);
        stored.AuthorName.ShouldBe(name);
    }

    [Fact]
    public async Task AJobNamesTheActorItWorksForOrElseTheSystem()
    {
        var actor = Of(PrincipalKind.AdminUser, "Scheduler");

        var forActor = await AsSystemAsync(Origin.Job, actor);
        var alone = await AsSystemAsync(Origin.Job, actor: null);

        forActor.AuthorId.ShouldBe(actor.Id);
        forActor.AuthorKind.ShouldBe(PrincipalKind.AdminUser);
        forActor.AuthorName.ShouldBe("Scheduler");
        forActor.Origin.ShouldBe(Origin.Job);
        alone.AuthorId.ShouldBe(Principal.System.Id);
        alone.AuthorKind.ShouldBe(PrincipalKind.System);
        alone.AuthorName.ShouldBe(Principal.System.Name);
    }

    [Fact]
    public async Task TheSeedIsNamedAsTheSystem()
    {
        var version = await AsSystemAsync(Origin.Seed, actor: null);

        version.AuthorId.ShouldBe(Guid.Empty);
        version.AuthorKind.ShouldBe(PrincipalKind.System);
        version.AuthorName.ShouldBe(Principal.System.Name);
        version.Origin.ShouldBe(Origin.Seed);
    }

    [Fact]
    public async Task AVersionKeepsTheNameItsAuthorHadWhenItWasWritten()
    {
        var id = Guid.CreateVersion7();
        var entry = await _database.CreateAsync(Of(PrincipalKind.AdminUser, "Ada", id), new() { ["title"] = "One" });

        await _database.PatchAsync(Of(PrincipalKind.AdminUser, "Ada Lovelace", id), IdOf(entry), new() { ["title"] = "Two" }, VersionOf(entry));

        var versions = await VersionsAsync(IdOf(entry));
        versions.Select(version => (version.Number, version.AuthorId, version.AuthorName)).ShouldBe([(2, id, "Ada Lovelace"), (1, id, "Ada")]);
    }

    [Fact]
    public async Task ANameLongerThanTheColumnIsCut()
    {
        var (_, version) = await SavedByAsync(Of(PrincipalKind.ServiceAccount, new string('n', 200)));

        version.AuthorName.ShouldBe(new string('n', 128));
    }

    private async Task<VersionInfo> AsSystemAsync(Origin origin, Principal? actor)
    {
        using var scope = _database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(Principal.System, origin, SiteA, "en", null, "job", DateTimeOffset.UtcNow) { Actor = actor };
        try
        {
            var content = scope.ServiceProvider.GetRequiredService<IContentService>();
            var entry = await content.CreateAsync("article", new() { ["title"] = $"T {Guid.CreateVersion7()}" }, Token);
            return (await content.GetVersionsAsync("article", IdOf(entry), cancellationToken: Token)).ShouldHaveSingleItem();
        }
        finally
        {
            accessor.Current = null;
        }
    }
}
