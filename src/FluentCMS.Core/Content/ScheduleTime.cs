using FluentCMS.Content;
using FluentCMS.Publishing;

namespace FluentCMS.Core.Content;

// Turns a request into the instant it runs. The zone's rules decide it on the day, so the same wall-clock time is a different
// instant either side of a change of clocks.
internal static class ScheduleTime
{
    private const int ZoneMaxLength = 64;

    public static (DateTimeOffset RunAt, string? Zone) Resolve(ScheduleRequest request, DateTimeOffset now)
    {
        if (!Enum.IsDefined(request.Action))
        {
            throw Refuse("action", ValidationErrorCodes.InvalidValue, "The action is not one that can be scheduled.");
        }

        string? zone = null;
        DateTimeOffset runAt;
        if (request.TimeZone is null)
        {
            if (request.At.Kind != DateTimeKind.Utc)
            {
                throw Refuse("at", ValidationErrorCodes.InvalidValue, "A time without a time zone must be UTC.");
            }

            runAt = new DateTimeOffset(request.At);
        }
        else
        {
            if (request.At.Kind != DateTimeKind.Unspecified)
            {
                throw Refuse("at", ValidationErrorCodes.InvalidValue, "A time in a time zone is the wall-clock time there, and has no kind.");
            }

            var info = Find(request.TimeZone);
            zone = request.TimeZone;
            if (info.IsInvalidTime(request.At))
            {
                throw Refuse("at", ContentErrorCodes.NonexistentLocalTime, $"That time does not exist in {zone}: the clocks skip it.");
            }

            // A time that happens twice runs the first time, when the offset is the larger.
            var offset = info.IsAmbiguousTime(request.At) ? info.GetAmbiguousTimeOffsets(request.At).Max() : info.GetUtcOffset(request.At);
            try
            {
                runAt = new DateTimeOffset(request.At, offset);
                _ = runAt.UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                throw Refuse("at", ValidationErrorCodes.InvalidValue, "The time is out of range.");
            }
        }

        if (runAt <= now)
        {
            throw Refuse("at", ContentErrorCodes.ScheduleNotInFuture, "The time is not in the future.");
        }

        return (runAt, zone);
    }

    private static TimeZoneInfo Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > ZoneMaxLength)
        {
            throw Refuse("timeZone", ValidationErrorCodes.InvalidValue, "The time zone is not valid.");
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Refuse("timeZone", ValidationErrorCodes.InvalidValue, "The time zone is not known.");
        }
    }

    private static ValidationException Refuse(string path, string code, string message) =>
        new([new ValidationError(path, code, message)]);
}
