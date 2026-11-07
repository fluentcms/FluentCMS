using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.ApiKeys;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using FluentCMS.Events;
using FluentCMS.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Content.SchedulingFixture;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Identity;

// A schedule an API key made checks the key as it is when the job runs, not the claims it had when it scheduled.
public sealed class ScheduledApiKeyTests : IDisposable
{
    private readonly SchedulingFixture _fixture = new(more: cms => cms.Services.AddScoped<IPrincipalRoles, UserPrincipalRoles>());

    public void Dispose() => _fixture.Dispose();

    private async Task<(Guid Id, Principal Principal)> KeyAsync(Guid role)
    {
        using var scope = _fixture.Database.Database.NewScope();
        CreatedApiKey created;
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, SiteA))
        {
            created = await scope.ServiceProvider.GetRequiredService<ApiKeyService>().CreateAsync(new ApiKeyRequest("scheduler", ApiKeyClass.Management, role), Token);
        }

        var principal = await scope.ServiceProvider.GetRequiredService<ApiKeyPrincipals>().CurrentAsync(created.Key.Id, Token);
        return (created.Key.Id, principal.ShouldNotBeNull());
    }

    private async Task<Guid> ScheduledAsync(Principal key)
    {
        var id = IdOf(await _fixture.Database.CreateAsync(key, new ContentDocument { ["title"] = "One" }));
        await _fixture.ScheduleAsync(key, id, At(Start.AddHours(1)));
        return id;
    }

    private async Task RunAsync()
    {
        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();
    }

    private void ShouldHaveFailed(Guid id)
    {
        var row = _fixture.Schedules(id).Single();
        row.Status.ShouldBe(ScheduleRow.Failed);
        row.Error.ShouldNotBeNull().ShouldContain("publish");
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Seen.Of<EntryPublishedEvent>().ShouldBeEmpty();
    }

    private Guid Role() => _fixture.RoleWith("content.article.create", "content.article.read", "content.article.publish");

    [Fact]
    public async Task AKeyThatIsStillValidPublishes()
    {
        var (_, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);

        await RunAsync();

        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Executed);
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task AKeyRevokedBeforeTheJobRanPublishesNothingAndTheScheduleFails()
    {
        var (keyId, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);
        using (var scope = _fixture.Database.Database.NewScope())
        using (SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, SiteA))
        {
            (await scope.ServiceProvider.GetRequiredService<ApiKeyService>().RevokeAsync(keyId, Token)).ShouldBeTrue();
        }

        await RunAsync();

        ShouldHaveFailed(id);
    }

    [Fact]
    public async Task AKeyThatExpiredBeforeTheJobRanPublishesNothing()
    {
        var (keyId, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);
        _fixture.Change(context => context.Set<ApiKey>().Single(row => row.Id == keyId).ExpiresAt = Start.AddMinutes(30));

        await RunAsync();

        ShouldHaveFailed(id);
    }

    [Fact]
    public async Task AKeyBoundElsewhereSinceItScheduledPublishesNothing()
    {
        var (keyId, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);
        _fixture.Schedules(id).Single().Claims.ShouldNotBeNull().ShouldNotContain(CmsClaimTypes.Sites);
        _fixture.Change(context => context.Set<ApiKey>().Single(row => row.Id == keyId).SiteIds = JsonSerializer.Serialize(new[] { SiteB.Value }));

        await RunAsync();

        ShouldHaveFailed(id);
    }

    [Fact]
    public async Task AKeyRoleReplacedByOneWithoutThePermissionPublishesNothing()
    {
        var (keyId, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);
        var reader = _fixture.RoleWith("content.article.read");
        _fixture.Change(context => context.Set<ApiKey>().Single(row => row.Id == keyId).RoleId = reader);

        await RunAsync();

        ShouldHaveFailed(id);
    }

    [Fact]
    public async Task AKeyThatIsGonePublishesNothing()
    {
        var (keyId, key) = await KeyAsync(Role());
        var id = await ScheduledAsync(key);
        _fixture.Change(context => context.Remove(context.Set<ApiKey>().Single(row => row.Id == keyId)));

        await RunAsync();

        ShouldHaveFailed(id);
    }
}
