using System.Globalization;
using FluentCMS.Content;
using FluentCMS.Core.Content;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity;

// What GET and PUT /me/preferences carry. A null value is one the user has not chosen.
internal sealed record UserPreferencesInfo(string? Culture, string? TimeZone, string? DateFormat, string? NumberFormat)
{
    public static readonly UserPreferencesInfo None = new(null, null, null, null);
}

// A user's admin preferences, kept in cms_user_preference so that they follow the user to every session and device (spec
// 4.10). The culture of the interface and those whose conventions dates and numbers follow are culture names the platform
// knows, stored as it writes them; the time zone is an IANA id, as a schedule takes. A write replaces them all.
internal sealed class UserPreferences(SystemDbContext context, TimeProvider clock)
{
    public const int CultureLength = 32;

    public const int TimeZoneLength = ScheduleTime.ZoneMaxLength;

    public async Task<UserPreferencesInfo> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        Info(await context.Set<UserPreferenceRow>().AsNoTracking().FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken));

    public async Task<UserPreferencesInfo> SetAsync(Guid userId, UserPreferencesInfo wanted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var valid = Validate(wanted);

        // Two devices that save the first preferences at once both insert; the second then updates the row the first made.
        for (var attempt = 0; ; attempt++)
        {
            var row = await context.Set<UserPreferenceRow>().FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);
            if (row is null)
            {
                row = new UserPreferenceRow { UserId = userId };
                context.Add(row);
            }

            row.Culture = valid.Culture;
            row.TimeZone = valid.TimeZone;
            row.DateFormat = valid.DateFormat;
            row.NumberFormat = valid.NumberFormat;
            row.UpdatedAt = clock.GetUtcNow();
            try
            {
                await context.SaveChangesAsync(cancellationToken);
                context.Entry(row).State = EntityState.Detached;
                return valid;
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                context.Entry(row).State = EntityState.Detached;
            }
        }
    }

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
