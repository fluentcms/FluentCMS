using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
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

// A scheduled publish asks cms_user_role, as it is when the job runs, which roles its scheduler holds.
public sealed class ScheduledRolesTests : IDisposable
{
    private readonly SchedulingFixture _fixture = new(more: cms => cms.Services.AddScoped<IPrincipalRoles, UserPrincipalRoles>());

    public void Dispose() => _fixture.Dispose();

    private async Task<(CmsUser User, Principal Principal)> StoredAsync(string name, params Guid[] roles)
    {
        using var scope = _fixture.Database.Database.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        var user = new CmsUser { UserName = name, Email = $"{name}@example.com" };
        IdentityFixture.Succeeded(await users.CreateAsync(user));
        IdentityFixture.Succeeded(await users.AddToRolesAsync(user, roles));
        return (user, IdentityFixture.PrincipalOf(user));
    }

    private async Task ChangeAsync(CmsUser user, Func<CmsUserManager, CmsUser, Task<Microsoft.AspNetCore.Identity.IdentityResult>> change)
    {
        using var scope = _fixture.Database.Database.NewScope();
        var users = scope.ServiceProvider.GetRequiredService<CmsUserManager>();
        IdentityFixture.Succeeded(await change(users, (await users.FindByIdAsync(user.Id.ToString("D")))!));
    }

    private async Task<Guid> DraftAsync()
    {
        var (_, editor) = await StoredAsync("editor", BuiltInRoles.Editor);
        return IdOf(await _fixture.Database.CreateAsync(editor, new ContentDocument { ["title"] = "One" }));
    }

    private Guid PublisherRole() => _fixture.RoleWith("content.article.read", "content.article.publish");

    [Fact]
    public async Task AStoredSchedulerThatStillHoldsTheRolePublishes()
    {
        var id = await DraftAsync();
        var (_, scheduler) = await StoredAsync("ann", PublisherRole());
        await _fixture.ScheduleAsync(scheduler, id, At(Start.AddHours(1)));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Executed);
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Published);
    }

    [Fact]
    public async Task ASchedulerThatLostTheRoleInCmsUserRolePublishesNothingAndEndsFailed()
    {
        var role = PublisherRole();
        var id = await DraftAsync();
        var (user, stored) = await StoredAsync("ann", role);

        // A principal whose claims name the role, as an older token might: the claims kept on the schedule are not what counts.
        var scheduler = new Principal(stored.Kind, stored.Id, stored.Name, new Dictionary<string, string>(stored.Claims) { [ClaimPrincipalRoles.Claim] = role.ToString() });
        await _fixture.ScheduleAsync(scheduler, id, At(Start.AddHours(1)));
        _fixture.Schedules(id).Single().Claims.ShouldNotBeNull().ShouldContain(role.ToString());
        await ChangeAsync(user, (users, stored) => users.RemoveFromRoleAsync(stored, role));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        var row = _fixture.Schedules(id).Single();
        row.Status.ShouldBe(ScheduleRow.Failed);
        row.Error.ShouldNotBeNull().ShouldContain("publish");
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
        _fixture.Seen.Of<EntryPublishedEvent>().ShouldBeEmpty();
        _fixture.Jobs().ShouldHaveSingleItem().Status.ShouldBe(JobStatus.Succeeded);
    }

    [Fact]
    public async Task ASchedulerDeactivatedBeforeTheJobRanPublishesNothing()
    {
        var id = await DraftAsync();
        var (user, scheduler) = await StoredAsync("ann", PublisherRole());
        await _fixture.ScheduleAsync(scheduler, id, At(Start.AddHours(1)));
        await ChangeAsync(user, (users, stored) => users.DeactivateAsync(stored));

        _fixture.AdvanceTo(Start.AddHours(1));
        (await _fixture.RunDueAsync()).ShouldBeTrue();

        _fixture.Schedules(id).Single().Status.ShouldBe(ScheduleRow.Failed);
        _fixture.Database.Document(id).Status.ShouldBe(EntryStatus.Draft);
    }
}
