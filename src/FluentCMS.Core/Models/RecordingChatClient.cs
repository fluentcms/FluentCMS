using System.Diagnostics;
using System.Text;
using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.AI;

namespace FluentCMS.Core.Models;

// The middleware that records every call the gateway makes, answered or not, with its verbatim prompt and response (MDL-04, CHK-05,
// AUD-04). One instance serves one call: ReserveAsync records it as pending at the most it can cost before anything is sent, so a
// call that cannot be recorded is never sent, and the answer, or the failure, then completes the record.
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

    // The most the call can cost: a token is at least one byte of text, so the prompt is no more tokens than it has bytes, and the
    // answer no more than the profile's output limit.
    public decimal Reserved => profile.Cost(Encoding.UTF8.GetByteCount(prompt.Text), profile.MaxOutputTokens);

    public async Task ReserveAsync(CancellationToken cancellationToken)
    {
        var (kind, id) = initiator is null ? ((string?)null, (Guid?)null) : InitiatorKinds.Describe(initiator);

        Recorded = new ModelCall
        {
            Id = Guid.CreateVersion7(),
            Timestamp = clock.GetUtcNow(),
            Profile = profile.Name,
            Provider = profile.Provider,
            Model = profile.Model,
            Purpose = purpose,
            InitiatorKind = kind,
            InitiatorId = id,
            AgentCredentialId = chargedTo,
            PromptHash = prompt.Hash,
            Prompt = prompt.Text,
            Cost = Reserved,
            Duration = TimeSpan.Zero,
            Outcome = ModelCallOutcome.Pending,
        };
        await log.AppendAsync(Recorded, cancellationToken);
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var pending = Recorded is { Outcome: ModelCallOutcome.Pending } reserved
            ? reserved
            : throw new InvalidOperationException("A call is reserved before it is sent, so it is on record whatever happens next.");
        var started = Stopwatch.GetTimestamp();

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception exception)
        {
            // A provider may charge for a call that failed or was cancelled after it was sent, so it keeps its reserved cost.
            Recorded = pending with { Outcome = ModelCallOutcome.Failed, Error = exception.Message, Duration = Stopwatch.GetElapsedTime(started) };
            await log.CompleteAsync(Recorded, CancellationToken.None);
            throw;
        }

        var input = response.Usage?.InputTokenCount ?? 0;
        var output = response.Usage?.OutputTokenCount ?? 0;
        var version = response.ModelId ?? profile.Model;
        Recorded = pending with
        {
            Outcome = ModelCallOutcome.Succeeded,
            ModelVersion = version.Length <= ModelCall.MaxNameLength ? version : version[..ModelCall.MaxNameLength],
            Response = response.Text,
            InputTokens = input,
            OutputTokens = output,
            Cost = profile.Cost(input, output),
            Duration = Stopwatch.GetElapsedTime(started),
        };
        await log.CompleteAsync(Recorded, CancellationToken.None);

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
}
