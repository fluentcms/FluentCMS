using FluentCMS.Content;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Events;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Data.Tests.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Data.Tests.Activity;

internal static class ActivityFixture
{
    // The test databases leave the activity log off, so that the tests of the event machinery see only their own handlers.
    public static Action<FluentCmsBuilder> On(Action<FluentCmsBuilder>? more = null) => cms =>
    {
        cms.Activity.Enabled = true;
        more?.Invoke(cms);
    };

    public static Task<int> DrainAsync(this ServiceDatabase database) =>
        database.Database.Services.GetRequiredService<OutboxDrainer>().DrainAsync(ServiceDatabase.Token);

    public static List<ActivityRow> ActivityRows(this ServiceDatabase database)
    {
        using var scope = database.Database.NewScope();
        return [.. scope.ServiceProvider.GetRequiredService<SystemDbContext>().Set<ActivityRow>().AsNoTracking().OrderBy(row => row.OccurredAt).ThenBy(row => row.Action)];
    }

    public static void Add(this ServiceDatabase database, params ActivityRow[] rows)
    {
        using var scope = database.Database.NewScope();
        var context = scope.ServiceProvider.GetRequiredService<SystemDbContext>();
        context.AddRange(rows);
        context.SaveChanges();
    }

    public static ActivityRow Row(string action, Guid? site, DateTimeOffset at, Guid? actor = null, Origin origin = Origin.Api, string? targetType = null, string? targetId = null) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            SiteId = site,
            OccurredAt = at,
            ActorType = PrincipalKind.AdminUser,
            ActorId = actor ?? Guid.Empty,
            ActorName = "Someone",
            Origin = origin,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            CorrelationId = "c",
        };
}
