using System.Text.Json;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Migrations;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Plugins;
using FluentCMS.Plugins.AiAssist;
using FluentCMS.Plugins.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Authorization;

// The permissions that providers name for the built-in roles by default, which a start of the host gives once per instance
// and records in cms_default_grant (#3103), on the provider of the run.
public sealed class DefaultGrantTests : IDisposable
{
    private static readonly string[] EditorSeed = ["content.*.create", "content.*.delete", "content.*.publish", "content.*.read", "content.*.restore", "content.*.unpublish", "content.*.update", "schema.read"];
    private static readonly string[] AuthorSeed = ["content.*.create", "content.*.delete", "content.*.read", "content.*.update", "schema.read"];

    private readonly TestDatabase _database = TestDatabase.Create();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ANewInstanceWithMediaAndAiAssistGivesTheBuiltInEditorAndAuthorTheirDefaults()
    {
        var state = await StartAsync(cms => cms.AddPlugin<MediaPlugin>().AddPlugin<AiAssistPlugin>());

        state.Grants[BuiltInRoles.Editor].ShouldBe([.. EditorSeed, "ai.use", "media.read", "media.update", "media.upload"], ignoreOrder: true);
        state.Grants[BuiltInRoles.Author].ShouldBe([.. AuthorSeed, "ai.use", "media.read", "media.upload"], ignoreOrder: true);
        state.Grants[BuiltInRoles.SuperAdmin].ShouldBe([CorePermissions.All]);
        state.Grants.GetValueOrDefault(BuiltInRoles.Public).ShouldBeNull();
        state.Grants.GetValueOrDefault(BuiltInRoles.Member).ShouldBeNull();
        state.Limited.ShouldBeEmpty();

        state.Records.ShouldBe(
        [
            (BuiltInRoles.Editor, "ai.use", "ai"), (BuiltInRoles.Editor, "media.read", "media"), (BuiltInRoles.Editor, "media.update", "media"), (BuiltInRoles.Editor, "media.upload", "media"),
            (BuiltInRoles.Author, "ai.use", "ai"), (BuiltInRoles.Author, "media.read", "media"), (BuiltInRoles.Author, "media.upload", "media"),
        ], ignoreOrder: true);

        // One save per start, which raises each changed role's stamp once, so that a cached set of its grants is read again.
        state.Versions[BuiltInRoles.Editor].ShouldBe(2);
        state.Versions[BuiltInRoles.Author].ShouldBe(2);
        state.Versions[BuiltInRoles.SuperAdmin].ShouldBe(1);

        state.Activity.ShouldBe(
        [
            ("role.updated", BuiltInRoles.Editor, "ai.use; media.read; media.update; media.upload"),
            ("role.updated", BuiltInRoles.Author, "ai.use; media.read; media.upload"),
        ], ignoreOrder: true);
    }

    [Fact]
    public async Task AGrantTakenAwayIsNotGivenAgainAtTheNextStart()
    {
        await StartAsync(cms => cms.AddPlugin<MediaPlugin>());
        await TakeAsync(BuiltInRoles.Editor, "media.update");

        var state = await StartAsync(cms => cms.AddPlugin<MediaPlugin>());

        state.Grants[BuiltInRoles.Editor].ShouldBe([.. EditorSeed, "media.read", "media.upload"], ignoreOrder: true);
        state.Records.Count.ShouldBe(5);
        state.Activity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task APluginAddedToAnInstanceGivesItsDefaultsOnceAtItsFirstStart()
    {
        var before = await StartAsync(_ => { });
        before.Grants[BuiltInRoles.Editor].ShouldBe(EditorSeed, ignoreOrder: true);
        before.Records.ShouldBeEmpty();

        var added = await StartAsync(cms => cms.AddPlugin<MediaPlugin>());
        var again = await StartAsync(cms => cms.AddPlugin<MediaPlugin>());

        added.Grants[BuiltInRoles.Editor].ShouldBe([.. EditorSeed, "media.read", "media.update", "media.upload"], ignoreOrder: true);
        added.Grants[BuiltInRoles.Author].ShouldBe([.. AuthorSeed, "media.read", "media.upload"], ignoreOrder: true);
        again.Grants.SelectMany(role => role.Value.Select(grant => (role.Key, grant))).ShouldBe(added.Grants.SelectMany(role => role.Value.Select(grant => (role.Key, grant))), ignoreOrder: true);
        again.Records.ShouldBe(added.Records, ignoreOrder: true);
        again.Versions.ShouldBe(added.Versions);
        again.Activity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ADefaultThatALaterVersionNamesIsGivenOnceAndTheEarlierOnesAreNotGivenAgain()
    {
        await StartAsync(cms => cms.AddPlugin<NotesPlugin>(), Version(1));
        await TakeAsync(BuiltInRoles.Editor, NotesPermissions.Read);

        var state = await StartAsync(cms => cms.AddPlugin<NotesPlugin>(), Version(2));

        state.Grants[BuiltInRoles.Editor].ShouldBe([.. EditorSeed, NotesPermissions.Write], ignoreOrder: true);
        state.Records.ShouldBe([(BuiltInRoles.Editor, NotesPermissions.Read, "notes"), (BuiltInRoles.Editor, NotesPermissions.Write, "notes")], ignoreOrder: true);
    }

    // An operator who limited a grant of the permission before the plugin's first start keeps that limit, and a grant with a
    // wildcard counts as holding the permission.
    [Fact]
    public async Task ARoleThatHoldsAGrantOfThePermissionKeepsWhatItHas()
    {
        await StartAsync(_ => { });
        await GiveAsync(BuiltInRoles.Editor, "media.read", new PermissionConstraints { Resources = ["folder-1"] });
        await GiveAsync(BuiltInRoles.Author, "media.*", null);

        var state = await StartAsync(cms => cms.AddPlugin<MediaPlugin>());

        state.Grants[BuiltInRoles.Editor].ShouldBe([.. EditorSeed, "media.read", "media.update", "media.upload"], ignoreOrder: true);
        state.Limited.ShouldBe([(BuiltInRoles.Editor, "media.read", "folder-1")]);
        state.Grants[BuiltInRoles.Author].ShouldBe([.. AuthorSeed, "media.*"], ignoreOrder: true);
        state.Records.Count.ShouldBe(5);
        state.Activity.ShouldBe([("role.updated", BuiltInRoles.Editor, "media.update; media.upload")]);
    }

    [Fact]
    public async Task TheDefaultsOfAProviderTheHostRegisteredItselfAreRecordedWithNoPlugin()
    {
        var state = await StartAsync(_ => { }, services => services.AddSingleton<IPermissionProvider, HostPermissions>());

        state.Grants[BuiltInRoles.Author].ShouldBe([.. AuthorSeed, HostPermissions.Export], ignoreOrder: true);
        state.Records.ShouldBe([(BuiltInRoles.Author, HostPermissions.Export, (string?)null)]);
    }

    private static Action<IServiceCollection> Version(int version) => services => services.AddSingleton(new NotesVersion(version));

    // Starts and stops a host of the instance, and reads what the start left.
    private async Task<InstanceState> StartAsync(Action<FluentCmsBuilder> configure, Action<IServiceCollection>? services = null)
    {
        using var host = _database.CreateHost(ArticleSchemas.NoTypes, "Production", services: services, configure: configure);
        await host.StartAsync(Token);
        await host.StopAsync(Token);

        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        var grants = await context.Set<RoleGrant>().AsNoTracking().ToListAsync(Token);
        var records = await context.Set<DefaultGrantRow>().AsNoTracking().ToListAsync(Token);
        var versions = await context.Set<Role>().AsNoTracking().Where(role => role.SiteId == null).ToDictionaryAsync(role => role.Id, role => role.PermissionsVersion, Token);
        var activity = await context.Set<ActivityRow>().AsNoTracking().Where(row => row.ActorType == PrincipalKind.System && row.Action == "role.updated").ToListAsync(Token);
        return new InstanceState(
            grants.GroupBy(grant => grant.RoleId).ToDictionary(group => group.Key, group => group.Select(grant => grant.Permission).Order(StringComparer.Ordinal).ToList()),
            [.. grants.Select(grant => (grant.RoleId, grant.Permission, PermissionConstraints.FromJson(grant.Constraints).Resources))
                .Where(grant => grant.Resources is not null)
                .Select(grant => (grant.RoleId, grant.Permission, string.Join(",", grant.Resources!)))],
            [.. records.Select(row => (row.RoleId, row.Permission, row.Plugin))],
            versions,
            [.. activity.Select(row => (row.Action, Guid.Parse(row.TargetId!), JsonDocument.Parse(row.Details!).RootElement.GetProperty("added").GetString()!))]);
    }

    private async Task TakeAsync(Guid role, string permission)
    {
        using var services = _database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.RemoveRange(await context.Set<RoleGrant>().Where(grant => grant.RoleId == role && grant.Permission == permission).ToListAsync(Token));
        await context.SaveChangesAsync(Token);
    }

    private async Task GiveAsync(Guid role, string permission, PermissionConstraints? constraints)
    {
        using var services = _database.CreateServices();
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        context.Add(new RoleGrant { Id = Guid.CreateVersion7(), RoleId = role, Permission = permission, Constraints = constraints?.ToJson() });
        await context.SaveChangesAsync(Token);
    }

    private sealed record InstanceState(
        Dictionary<Guid, List<string>> Grants,
        List<(Guid Role, string Permission, string Resources)> Limited,
        List<(Guid Role, string Permission, string? Plugin)> Records,
        Dictionary<Guid, long> Versions,
        List<(string Action, Guid Role, string Added)> Activity);
}

public sealed record NotesVersion(int Value);

// A plugin whose second version names a default for a permission that its first version gave to no role.
[FluentCmsPlugin("notes", SdkRange = "[1.0.0,2.0.0)")]
public sealed class NotesPlugin : IPlugin
{
    public void ConfigureServices(IPluginServiceBuilder plugin) => plugin.AddPermissions<NotesPermissions>();

    public void Configure(IPluginAppBuilder app)
    {
    }
}

public sealed class NotesPermissions(NotesVersion version) : IPermissionProvider
{
    public const string Read = "notes.read";
    public const string Write = "notes.write";

    public IEnumerable<Permission> GetPermissions() =>
    [
        new Permission(Read, "Read notes.") { DefaultRoles = [BuiltInRole.Editor] },
        new Permission(Write, "Write notes.") { DefaultRoles = version.Value > 1 ? [BuiltInRole.Editor] : [] },
    ];
}

public sealed class HostPermissions : IPermissionProvider
{
    public const string Export = "reports.export";

    public IEnumerable<Permission> GetPermissions() => [new Permission(Export, "Export reports.") { DefaultRoles = [BuiltInRole.Author] }];
}
