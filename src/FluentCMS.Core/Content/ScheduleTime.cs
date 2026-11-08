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
        if (request.Instant is { } instant)
        {
            runAt = instant.ToUniversalTime();
        }
        else
        {
            var local = request.LocalTime!.Value;
            var info = Find(request.TimeZone!);
            zone = request.TimeZone;
            if (info.IsInvalidTime(local))
            {
                throw Refuse("at", ContentErrorCodes.NonexistentLocalTime, $"That time does not exist in {zone}: the clocks skip it.");
            }

            // A time that happens twice runs the first time, when the offset is the larger.
            var offset = info.IsAmbiguousTime(local) ? info.GetAmbiguousTimeOffsets(local).Max() : info.GetUtcOffset(local);
            try
            {
                runAt = new DateTimeOffset(local, offset);
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

        TimeZoneInfo info;
        try
        {
            info = TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw Refuse("timeZone", ValidationErrorCodes.InvalidValue, "The time zone is not known.");
        }

        // Each platform also finds the zones by their Windows ids, which the other does not hold, so only an IANA id is taken.
        if (!info.HasIanaId && !TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _))
        {
            throw Refuse("timeZone", ValidationErrorCodes.InvalidValue, "The time zone is an IANA id, such as Europe/Berlin.");
        }

        return info;
    }

    private static ValidationException Refuse(string path, string code, string message) =>
        new([new ValidationError(path, code, message)]);
}
