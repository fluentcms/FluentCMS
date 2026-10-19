using System.Globalization;
using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Checks;

// The facts of an entity are still true (CHK-01, P3): none is past its 'validUntil', and with 'maxAgeDays' each was verified within
// that many days (SCH-03). Content that only references facts is checked against the entity once references resolve (F-ENT).
internal sealed class StalenessCheckType : ICheckType
{
    public const int MaxAgeDays = 36500;

    public string Name => "staleness";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        if (reader.PositiveInteger("maxAgeDays") > MaxAgeDays)
        {
            reader.Invalid("maxAgeDays", $"must be at most {MaxAgeDays}, a hundred years");
        }

        return reader.Errors();
    }

    public Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken)
    {
        var maxAgeDays = new CheckSettingsReader(Name, input.Settings).PositiveInteger("maxAgeDays");
        var now = (input.Services.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var findings = new List<CheckFinding>();

        foreach (var (field, fact) in CheckContent.Facts(input.Subject))
        {
            var pointer = CheckContent.Append(string.Empty, field);
            var validUntil = Time(fact, FactMetadata.ValidUntil, field, pointer, findings);
            var verified = Time(fact, FactMetadata.LastVerified, field, pointer, findings);

            if (validUntil < now)
            {
                findings.Add(new CheckFinding(
                    $"Verify {field} and give it a new validUntil, or replace it; it was valid until {Day(validUntil.Value)}.",
                    CheckContent.Append(pointer, FactMetadata.ValidUntil)));
            }

            if (verified > now)
            {
                findings.Add(new CheckFinding(
                    $"Correct the lastVerified of {field}; {Day(verified.Value)} has not happened yet.",
                    CheckContent.Append(pointer, FactMetadata.LastVerified)));
            }
            else if (maxAgeDays is not null && verified is null && !HasMetadata(fact, FactMetadata.LastVerified))
            {
                findings.Add(new CheckFinding($"Verify {field} and set its lastVerified; it has never been verified.", pointer));
            }
            else if (maxAgeDays is { } maxDays && verified < now.AddDays(-maxDays))
            {
                findings.Add(new CheckFinding(
                    $"Verify {field} again and update its lastVerified; it was last verified on {Day(verified.Value)}, more than {maxDays} days ago.",
                    CheckContent.Append(pointer, FactMetadata.LastVerified)));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    // A time that cannot be read is reported, so a fact does not look current because its date is garbled.
    private static DateTimeOffset? Time(JsonElement fact, string name, string field, string pointer, List<CheckFinding> findings)
    {
        if (CheckContent.Metadata(fact, name) is not { } value)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && IsoDateTimes.IsPointInTime(value.GetString())
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        findings.Add(new CheckFinding(
            $"Correct the {name} of {field} to a point in time with an offset, such as 2026-01-31T09:30:00Z.",
            CheckContent.Append(pointer, name)));
        return null;
    }

    private static bool HasMetadata(JsonElement fact, string name)
    {
        return CheckContent.Metadata(fact, name) is not null;
    }

    private static string Day(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
