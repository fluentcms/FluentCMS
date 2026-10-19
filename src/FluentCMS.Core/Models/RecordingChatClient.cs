using System.Diagnostics;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.AI;

namespace FluentCMS.Core.Models;

// The middleware that records every call the gateway makes, answered or not, with its verbatim prompt and response (MDL-04, CHK-05,
// AUD-04). One instance serves one call, so it knows the call's profile, purpose and prompt.
internal sealed class RecordingChatClient(
    IChatClient inner,
    IModelCallLog log,
    ModelProfile profile,
    string purpose,
    ModelPrompt prompt,
    Initiator? initiator,
    Guid? chargedTo,
    TimeProvider clock) : DelegatingChatClient(inner)
{
    public ModelCall? Recorded { get; private set; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var timestamp = clock.GetUtcNow();
        var started = Stopwatch.GetTimestamp();

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception exception)
        {
            // A provider may charge for a call that failed or was cancelled after it was sent, so it is on record either way.
            Recorded = Call(timestamp, started) with { Outcome = ModelCallOutcome.Failed, Error = exception.Message };
            await log.AppendAsync(Recorded, CancellationToken.None);
            throw;
        }

        var input = response.Usage?.InputTokenCount ?? 0;
        var output = response.Usage?.OutputTokenCount ?? 0;
        Recorded = Call(timestamp, started) with
        {
            Outcome = ModelCallOutcome.Succeeded,
            ModelVersion = response.ModelId ?? profile.Model,
            Response = response.Text,
            InputTokens = input,
            OutputTokens = output,
            Cost = profile.Cost(input, output),
        };
        await log.AppendAsync(Recorded, CancellationToken.None);

        return response;
    }

    // A streamed answer would reach the caller before it is recorded, so the gateway does not offer one.
    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("The model gateway records whole answers, so it does not stream.");
    }

    private ModelCall Call(DateTimeOffset timestamp, long started)
    {
        var (kind, id) = initiator is null ? ((string?)null, (Guid?)null) : InitiatorKinds.Describe(initiator);

        return new ModelCall
        {
            Id = Guid.CreateVersion7(),
            Timestamp = timestamp,
            Profile = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            Purpose = purpose,
            InitiatorKind = kind,
            InitiatorId = id,
            AgentCredentialId = chargedTo,
            PromptHash = prompt.Hash,
            Prompt = prompt.Text,
            Duration = Stopwatch.GetElapsedTime(started),
            Outcome = ModelCallOutcome.Failed,
        };
    }
}
