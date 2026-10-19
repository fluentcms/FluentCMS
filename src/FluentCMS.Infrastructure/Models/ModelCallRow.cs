using FluentCMS.Core.Models;

namespace FluentCMS.Infrastructure.Models;

// A ModelCall in the ModelCalls table, the prompt and response store (AUD-04). Rows are inserted once; a purge clears Prompt and
// Response and sets PurgedAt, and nothing else changes them.
public sealed class ModelCallRow
{
    public Guid Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string Profile { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string? ModelVersion { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public string? InitiatorKind { get; set; }

    public Guid? InitiatorId { get; set; }

    public Guid? AgentCredentialId { get; set; }

    public string PromptHash { get; set; } = string.Empty;

    public string? Prompt { get; set; }

    public string? Response { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public decimal Cost { get; set; }

    public TimeSpan Duration { get; set; }

    public ModelCallOutcome Outcome { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset? PurgedAt { get; set; }

    internal static ModelCallRow From(ModelCall call)
    {
        return new ModelCallRow
        {
            Id = call.Id,
            Timestamp = call.Timestamp,
            Profile = call.Profile,
            Provider = call.Provider,
            Model = call.Model,
            ModelVersion = call.ModelVersion,
            Purpose = call.Purpose,
            InitiatorKind = call.InitiatorKind,
            InitiatorId = call.InitiatorId,
            AgentCredentialId = call.AgentCredentialId,
            PromptHash = call.PromptHash,
            Prompt = call.Prompt,
            Response = call.Response,
            InputTokens = call.InputTokens,
            OutputTokens = call.OutputTokens,
            Cost = call.Cost,
            Duration = call.Duration,
            Outcome = call.Outcome,
            Error = call.Error,
            PurgedAt = call.PurgedAt,
        };
    }

    internal ModelCall ToCall()
    {
        return new ModelCall
        {
            Id = Id,
            Timestamp = Timestamp,
            Profile = Profile,
            Provider = Provider,
            Model = Model,
            ModelVersion = ModelVersion,
            Purpose = Purpose,
            InitiatorKind = InitiatorKind,
            InitiatorId = InitiatorId,
            AgentCredentialId = AgentCredentialId,
            PromptHash = PromptHash,
            Prompt = Prompt,
            Response = Response,
            InputTokens = InputTokens,
            OutputTokens = OutputTokens,
            Cost = Cost,
            Duration = Duration,
            Outcome = Outcome,
            Error = Error,
            PurgedAt = PurgedAt,
        };
    }
}
