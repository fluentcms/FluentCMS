using System.Globalization;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.Localization;

namespace FluentCMS.Admin.Api;

// The notice after a 429 the client could not wait out, read out at once. The live regions take the announcement onto the
// renderer themselves, so it may come from inside the HTTP pipeline. A burst of refused requests is told once: not again
// within Quiet of the last notice.
internal sealed class RateLimitNotice(IAnnouncer announcer, IStringLocalizer<AdminResources> texts, TimeProvider time) : IRateLimitNotice
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(10);

    private readonly Lock _gate = new();
    private DateTimeOffset? _last;

    public void TooManyRequests(TimeSpan? retryAfter)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_last is { } last && now - last < Quiet)
            {
                return;
            }

            _last = now;
        }

        var message = retryAfter is { } wait && wait > TimeSpan.Zero
            ? texts["Api.RateLimit.WaitSeconds", Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.CurrentCulture)]
            : texts["Api.RateLimit.WaitShort"];
        announcer.Announce(message, AnnouncementPoliteness.Assertive);
    }
}
