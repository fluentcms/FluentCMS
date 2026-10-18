using FluentCMS.Core.Results;

namespace FluentCMS.Core.Agents;

// The limits on one credential (MCP-05, SEC-04). Every cap is required, so no credential is unlimited by omission.
public sealed record AgentCaps
{
    private AgentCaps(
        int callsPerPeriod,
        TimeSpan callPeriod,
        decimal llmSpendPerPeriod,
        TimeSpan llmSpendPeriod,
        int itemsPerChangeSet,
        int changeSetsPerDay)
    {
        CallsPerPeriod = callsPerPeriod;
        CallPeriod = callPeriod;
        LlmSpendPerPeriod = llmSpendPerPeriod;
        LlmSpendPeriod = llmSpendPeriod;
        ItemsPerChangeSet = itemsPerChangeSet;
        ChangeSetsPerDay = changeSetsPerDay;
    }

    public int CallsPerPeriod { get; }

    public TimeSpan CallPeriod { get; }

    // In US dollars, the unit model providers price in. Zero allows no model spend at all.
    public decimal LlmSpendPerPeriod { get; }

    public TimeSpan LlmSpendPeriod { get; }

    public int ItemsPerChangeSet { get; }

    public int ChangeSetsPerDay { get; }

    public static Result<AgentCaps> Create(
        int callsPerPeriod,
        TimeSpan callPeriod,
        decimal llmSpendPerPeriod,
        TimeSpan llmSpendPeriod,
        int itemsPerChangeSet,
        int changeSetsPerDay)
    {
        var errors = new List<Error>();

        AtLeastOne(errors, callsPerPeriod, "Calls per period");
        WholeSeconds(errors, callPeriod, "The call period");
        if (llmSpendPerPeriod < 0)
        {
            errors.Add(new Error(AgentCredentialErrorCodes.CapInvalid, "LLM spend per period cannot be negative; use 0 to allow no model spend."));
        }

        WholeSeconds(errors, llmSpendPeriod, "The LLM spend period");
        AtLeastOne(errors, itemsPerChangeSet, "Items per change set");
        AtLeastOne(errors, changeSetsPerDay, "Change sets per day");

        return errors.Count == 0
            ? Result<AgentCaps>.Success(new AgentCaps(callsPerPeriod, callPeriod, llmSpendPerPeriod, llmSpendPeriod, itemsPerChangeSet, changeSetsPerDay))
            : Result<AgentCaps>.Failure(errors);
    }

    // Rebuilds stored caps without checking them again, so they stay readable if the rules above change.
    public static AgentCaps Restore(
        int callsPerPeriod,
        TimeSpan callPeriod,
        decimal llmSpendPerPeriod,
        TimeSpan llmSpendPeriod,
        int itemsPerChangeSet,
        int changeSetsPerDay)
    {
        return new AgentCaps(callsPerPeriod, callPeriod, llmSpendPerPeriod, llmSpendPeriod, itemsPerChangeSet, changeSetsPerDay);
    }

    private static void AtLeastOne(List<Error> errors, int value, string name)
    {
        if (value < 1)
        {
            errors.Add(new Error(AgentCredentialErrorCodes.CapInvalid, $"{name} must be at least 1, and {value} was given."));
        }
    }

    // Whole seconds read back the same on both providers; PostgreSQL keeps intervals to the microsecond.
    private static void WholeSeconds(List<Error> errors, TimeSpan period, string name)
    {
        if (period < TimeSpan.FromSeconds(1) || period.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            errors.Add(new Error(AgentCredentialErrorCodes.CapInvalid, $"{name} must be a whole number of seconds, at least one, and {period} was given."));
        }
    }
}
