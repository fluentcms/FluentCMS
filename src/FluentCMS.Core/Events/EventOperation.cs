using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Events;
using FluentCMS.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Events;

// The operation a handler runs under when no request does: as a job runs, the system principal with the actor of the event
// for audit, on the site of the event, in that site's default locale.
internal static class EventOperation
{
    public static async Task<OperationContext> CreateAsync(IServiceProvider services, EventSource source, Guid eventId, Guid? rowSite, DateTimeOffset now)
    {
        var site = rowSite ?? (source.SiteId == Guid.Empty ? DefaultSite.Id : source.SiteId);
        var locale = await services.GetRequiredService<SystemDbContext>().Set<Site>()
            .Where(row => row.Id == site)
            .Select(row => row.DefaultLocale)
            .FirstOrDefaultAsync();

        return new OperationContext(
            Principal.System,
            Origin.Job,
            new SiteId(site),
            locale ?? DefaultSite.Locale,
            ClientId: null,
            string.IsNullOrEmpty(source.CorrelationId) ? eventId.ToString() : source.CorrelationId,
            now)
        {
            Actor = ActorOf(source),
        };
    }

    private static Principal? ActorOf(EventSource source)
    {
        if (source.ActorId == Guid.Empty || source.ActorKind is PrincipalKind.System or PrincipalKind.Anonymous)
        {
            return null;
        }

        try
        {
            return new Principal(source.ActorKind, source.ActorId, source.ActorName ?? "");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
