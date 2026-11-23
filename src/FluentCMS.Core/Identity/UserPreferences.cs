using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// What GET, PUT and PATCH /me/preferences carry. A null value is one the user has not chosen.
internal sealed record UserPreferencesInfo(string? Culture, string? TimeZone, string? DateFormat, string? NumberFormat)
{
    public static readonly UserPreferencesInfo None = new(null, null, null, null);
}

// The members of the preferences a write sets: PUT sets them all, PATCH those its body names, so that the others keep what is
// stored, whatever the caller last read. A member set to null is no longer chosen.
internal sealed record UserPreferencesChange
{
    public required UserPreferencesInfo Values { get; init; }

    public bool SetsCulture { get; init; }

    public bool SetsTimeZone { get; init; }

    public bool SetsDateFormat { get; init; }

    public bool SetsNumberFormat { get; init; }

    public bool SetsAny => SetsCulture || SetsTimeZone || SetsDateFormat || SetsNumberFormat;

    public static UserPreferencesChange All(UserPreferencesInfo values) =>
        new() { Values = values, SetsCulture = true, SetsTimeZone = true, SetsDateFormat = true, SetsNumberFormat = true };
}

// A user's admin preferences, kept in cms_user_preference so that they follow the user to every session and device (spec
// 4.10). The culture of the interface and those whose conventions dates and numbers follow are culture names the platform
// knows, stored as it writes them; the time zone is an IANA id, as a schedule takes. The row has no version, so the entity
// tag is a hash of the four values: it changes with any of them, and a write based on a tag is refused once another write
// has changed them.
internal sealed class UserPreferences(SystemDbContext context, TimeProvider clock)
{
    public const int CultureLength = 32;

    public const int TimeZoneLength = ScheduleTime.ZoneMaxLength;

    public static string Tag(UserPreferencesInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var values = JsonSerializer.SerializeToUtf8Bytes(new[] { info.Culture, info.TimeZone, info.DateFormat, info.NumberFormat });
        return Convert.ToHexStringLower(SHA256.HashData(values).AsSpan(0, 16));
    }

    public async Task<UserPreferencesInfo> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Info(await context.Set<UserPreferenceRow>().AsNoTracking().FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken));

    // expectedTag is the tag the write was based on, or null for whatever is stored.
    public async Task<UserPreferencesInfo> SetAsync(Guid userId, UserPreferencesChange change, string? expectedTag, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var valid = Validate(change.Values);

        // Two devices that save the first preferences at once both insert; the second then updates the row the first made.
        for (var attempt = 0; ; attempt++)
        {
            var row = await context.Set<UserPreferenceRow>().AsNoTracking().FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            var stored = Info(row);
            if (expectedTag is not null && !string.Equals(expectedTag, Tag(stored), StringComparison.Ordinal))
            {
                throw Conflict();
            }

            // A change that names no member writes nothing, not even a row for a user who has none.
            if (!change.SetsAny)
            {
                return stored;
            }

            var next = new UserPreferencesInfo(
                change.SetsCulture ? valid.Culture : stored.Culture,
                change.SetsTimeZone ? valid.TimeZone : stored.TimeZone,
                change.SetsDateFormat ? valid.DateFormat : stored.DateFormat,
                change.SetsNumberFormat ? valid.NumberFormat : stored.NumberFormat);
            if (row is null)
            {
                var added = new UserPreferenceRow
                {
                    UserId = userId,
                    Culture = next.Culture,
                    TimeZone = next.TimeZone,
                    DateFormat = next.DateFormat,
                    NumberFormat = next.NumberFormat,
                    UpdatedAt = clock.GetUtcNow(),
                };
                context.Add(added);
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return next;
                }
                catch (DbUpdateException) when (attempt == 0)
                {
                    continue;
                }
                finally
                {
                    context.Entry(added).State = EntityState.Detached;
                }
            }

            // Only the members the write sets are written, so that two writes of different members both stay. With a tag the
            // write holds only while the row still has the values the tag was checked against. The answer is the row as this
            // update left it, read again in its transaction, since a write of other members may have landed after the read
            // above; the update holds the row until the commit, so no later write shows in the answer either.
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var rows = context.Set<UserPreferenceRow>().Where(item => item.UserId == userId);
            if (expectedTag is not null)
            {
                rows = rows.Where(item => item.Culture == stored.Culture && item.TimeZone == stored.TimeZone
                    && item.DateFormat == stored.DateFormat && item.NumberFormat == stored.NumberFormat);
            }

            var now = clock.GetUtcNow();
            var written = await rows.ExecuteUpdateAsync(
                set =>
                {
                    set.SetProperty(item => item.UpdatedAt, now);
                    if (change.SetsCulture)
                    {
                        set.SetProperty(item => item.Culture, next.Culture);
                    }

                    if (change.SetsTimeZone)
                    {
                        set.SetProperty(item => item.TimeZone, next.TimeZone);
                    }

                    if (change.SetsDateFormat)
                    {
                        set.SetProperty(item => item.DateFormat, next.DateFormat);
                    }

                    if (change.SetsNumberFormat)
                    {
                        set.SetProperty(item => item.NumberFormat, next.NumberFormat);
                    }
                },
                cancellationToken);
            if (written > 0)
            {
                var updated = Info(await context.Set<UserPreferenceRow>().AsNoTracking().FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken));
                await transaction.CommitAsync(cancellationToken);
                return updated;
            }

            if (expectedTag is not null)
            {
                throw Conflict();
            }
        }
    }

    private static ValidationException Conflict() =>
        new([new ValidationError("", ApiErrorCodes.VersionConflict, "The preferences changed since they were read; read them again and retry.")]);

    private static UserPreferencesInfo Validate(UserPreferencesInfo wanted)
    {
        List<ValidationError> errors = [];
        var culture = Culture(wanted.Culture, "culture", errors);
        var dateFormat = Culture(wanted.DateFormat, "dateFormat", errors);
        var numberFormat = Culture(wanted.NumberFormat, "numberFormat", errors);

        var timeZone = wanted.TimeZone;
        if (timeZone is not null)
        {
            try
            {
                // The platform finds a zone it has already found in any case, so only the exact id is taken, whatever came before.
                var zone = ScheduleTime.FindZone(timeZone, "timeZone");
                if (!zone.HasIanaId || !string.Equals(zone.Id, timeZone, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError("timeZone", ValidationErrorCodes.InvalidValue, "The time zone is an IANA id, such as Europe/Berlin."));
                }
            }
            catch (ValidationException refused)
            {
                errors.AddRange(refused.Errors);
            }
        }

        return errors.Count > 0 ? throw new ValidationException(errors) : new UserPreferencesInfo(culture, timeZone, dateFormat, numberFormat);
    }

    // A culture the platform has data for, not the invariant one, in the case the platform writes it (de-DE for de-de).
    private static string? Culture(string? name, string path, List<ValidationError> errors)
    {
        if (name is null)
        {
            return null;
        }

        if (name.Length is 0 or > CultureLength || name.Trim().Length != name.Length)
        {
            errors.Add(new ValidationError(path, ValidationErrorCodes.InvalidValue, $"A culture name has 1 to {CultureLength} characters, such as en-US."));
            return null;
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            if (culture.Name.Length > 0)
            {
                return culture.Name;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        errors.Add(new ValidationError(path, ValidationErrorCodes.InvalidValue, $"'{name}' is not a culture this server knows, such as en-US."));
        return null;
    }

    private static UserPreferencesInfo Info(UserPreferenceRow? row) =>
        row is null ? UserPreferencesInfo.None : new(row.Culture, row.TimeZone, row.DateFormat, row.NumberFormat);
}
