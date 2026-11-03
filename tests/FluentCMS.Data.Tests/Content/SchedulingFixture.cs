using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Events;
using FluentCMS.Data.Tests.Jobs;
using FluentCMS.Events;
using FluentCMS.Publishing;
using FluentCMS.Schema;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.Data.Tests.Content.ServiceDatabase;

namespace FluentCMS.Data.Tests.Content;

internal sealed class Heard<TEvent>(Seen seen) : IEventHandler<TEvent>
    where TEvent : CmsEvent
{
    public ValueTask HandleAsync(TEvent message, CancellationToken cancellationToken) => seen.HeardAsync(typeof(TEvent).Name, message, null, cancellationToken);
}

// The real service on a real SQLite file with a clock that moves only when a test says so, so that a schedule comes due when
// the test advances the clock and the job runner is asked to run what is due.
internal sealed class SchedulingFixture : IDisposable
{
    public static readonly DateTimeOffset Start = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);

    public SchedulingFixture(SchemaModel? schema = null, Action<FluentCmsBuilder>? more = null, bool activity = false)
    {
        Database = new ServiceDatabase(
            cms =>
            {
                cms.Activity.Enabled = activity;
                cms.Services.AddSingleton<TimeProvider>(Clock);
                cms.Services.AddSingleton(Seen);
                cms.Events.AddHandler<EntryPublishedEvent, Heard<EntryPublishedEvent>>();
                cms.Events.AddHandler<EntryUnpublishedEvent, Heard<EntryUnpublishedEvent>>();
                cms.Events.AddHandler<EntryScheduledEvent, Heard<EntryScheduledEvent>>();
                cms.Events.AddHandler<EntryScheduleCancelledEvent, Heard<EntryScheduleCancelledEvent>>();
                more?.Invoke(cms);
            },
            schema);
    }

    public ManualTimeProvider Clock { get; } = new(Start);

    public Seen Seen { get; } = new();

    public ServiceDatabase Database { get; }

    public static ScheduleRequest At(DateTimeOffset when, ScheduleAction action = ScheduleAction.Publish) => new(action, when.UtcDateTime);

    public Task<ScheduleInfo> ScheduleAsync(Principal principal, Guid id, ScheduleRequest request, string? locale = null, string type = "article", SiteId? site = null) =>
        Database.AsAsync(principal, services => services.GetRequiredService<IContentService>().ScheduleAsync(type, id, request, locale, Token), site: site);

    public Task CancelAsync(Principal principal, Guid id, ScheduleAction action = ScheduleAction.Publish, string? locale = null, string type = "article") =>
        Database.AsAsync<object?>(
            principal,
            async services =>
            {
                await services.GetRequiredService<IContentService>().CancelScheduleAsync(type, id, action, locale, Token);
                return null;
            });

    public async Task<Guid> DraftAsync(string title = "One", ContentDocument? more = null)
    {
        ContentDocument entry = new(more ?? []) { ["title"] = title };
        return IdOf(await Database.CreateAsync(Editor, entry));
    }

    public async Task<Guid> PublishedAsync(string title = "One")
    {
        var id = await DraftAsync(title);
        await Database.PublishAsync(Editor, id);
        return id;
    }

    public void AdvanceTo(DateTimeOffset when) => Clock.Advance(when - Clock.GetUtcNow());

    // One round of the job runner on the default queue: false when nothing is due.
    public async Task<bool> RunDueAsync()
    {
        using var scope = Database.Database.NewScope();
        return await scope.ServiceProvider.GetRequiredService<JobExecutor>().RunNextAsync(JobRunnerOptions.DefaultQueue, "test", Token);
    }

    // The job body on its own, under the operation the runner gives it, for a job that is not the one a schedule names or that fails.
    public async Task<ScheduleOutcome> RunAsync(Guid schedule, Guid job, Principal? actor, ScheduleAction action = ScheduleAction.Publish, bool lastAttempt = false)
    {
        using var scope = Database.Database.NewScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        accessor.Current = new OperationContext(Principal.System, Origin.Job, SiteA, "en", null, "test", DateTimeOffset.UtcNow)
        {
            Actor = actor is null ? null : new Principal(actor.Kind, actor.Id, actor.Name),
        };

        try
        {
            return await scope.ServiceProvider.GetRequiredService<ContentService>().RunScheduledAsync(schedule, job, action, lastAttempt, Token);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    public Principal Publisher(params string[] more) => WithRole(RoleWith(["content.article.read", "content.article.publish", "content.article.unpublish", .. more]));

    public Guid RoleWith(params string[] permissions) =>
        Database.Database.AddRoleForTest(Guid.NewGuid().ToString("N"), [.. permissions.Select(permission => (permission, (PermissionConstraints?)null))]);

    public List<ScheduleRow> Schedules(Guid id) =>
        Database.Read(context => context.Set<ScheduleRow>().AsNoTracking().Where(row => row.DocumentId == id).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList());

    public List<Job> Jobs() =>
        Database.Read(context => context.Set<Job>().AsNoTracking().Where(job => job.Type.StartsWith("content.schedule")).OrderBy(job => job.CreatedAt).ThenBy(job => job.Id).ToList());

    public void RemoveGrant(Guid role, string permission) => Change(context =>
    {
        context.Remove(context.Set<RoleGrant>().Single(grant => grant.RoleId == role && grant.Permission == permission));
    });

    public void RemoveRole(Guid role) => Change(context => context.Remove(context.Set<Role>().Single(item => item.Id == role)));

    public void Change(Action<CmsDbContext> change)
    {
        using var scope = Database.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        change(context);
        context.SaveChanges();
    }

    public void Dispose() => Database.Dispose();
}
