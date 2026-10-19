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
    public string Name => "staleness";

    public CheckKind Kind => CheckKind.Deterministic;

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings)
    {
        var reader = new CheckSettingsReader(Name, settings);
        reader.PositiveInteger("maxAgeDays");
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

            if (Time(fact, FactMetadata.ValidUntil) is { } validUntil && validUntil < now)
            {
                findings.Add(new CheckFinding(
                    $"Verify {field} and give it a new validUntil, or replace it; it was valid until {Day(validUntil)}.",
                    CheckContent.Append(pointer, FactMetadata.ValidUntil)));
            }

            if (maxAgeDays is not { } days)
            {
                continue;
            }

            var verified = Time(fact, FactMetadata.LastVerified);
            if (verified is null)
            {
                findings.Add(new CheckFinding($"Verify {field} and set its lastVerified; it has never been verified.", pointer));
            }
            else if (verified < now.AddDays(-days))
            {
                findings.Add(new CheckFinding(
                    $"Verify {field} again and update its lastVerified; it was last verified on {Day(verified.Value)}, more than {days} days ago.",
                    CheckContent.Append(pointer, FactMetadata.LastVerified)));
            }
        }

        return Task.FromResult(findings.Count == 0 ? CheckOutcome.Pass() : CheckOutcome.Fail(findings));
    }

    private static DateTimeOffset? Time(JsonElement fact, string name)
    {
        return fact.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && IsoDateTimes.IsPointInTime(value.GetString())
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time
                : null;
    }

    private static string Day(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
