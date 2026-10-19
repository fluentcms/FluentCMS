using System.Collections.Concurrent;
using System.Globalization;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.Extensions.AI;

namespace FluentCMS.Core.Models;

// The checks that decide whether a call may go out come first and return results. A call that may go out is recorded as pending
// before it is sent, and the provider's answer or failure completes the record, so every call that is sent is on record.
internal sealed class ModelGateway(
    IModelProfiles profiles,
    IModelClients clients,
    IModelCallLog log,
    IOutboundHosts outbound,
    IAgentCredentialDirectory agents,
    ICurrentInitiator currentInitiator,
    TimeProvider clock) : IModelGateway
{
    // One agent's spend check and reservation run one at a time in this process, so calls made at once cannot all pass a cap that
    // only one of them fits under.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SpendLocks = new();

    public bool IsAvailable(string profile)
    {
        return Refusal(profile) is null;
    }

    public async Task<Result<ModelAnswer>> AskAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Purpose.Length, ModelCall.MaxNameLength, nameof(request));

        if (Refusal(request.Profile) is { } refused)
        {
            return Result<ModelAnswer>.Failure([refused]);
        }

        var profile = profiles.Find(request.Profile)!;
        var initiator = currentInitiator.Current;
        var chargedTo = request.ChargedTo ?? (initiator as AgentInitiator)?.CredentialId;
        var prompt = ModelPrompt.Build(request.Instructions, request.Data);

        // Not disposed: disposing it would dispose the provider client, which the wiring keeps for the profile's next call.
        var client = new RecordingChatClient(clients.Create(profile), log, profile, request.Purpose, prompt, initiator, chargedTo, clock);
        if (await ReserveAsync(client, chargedTo, cancellationToken) is { } overCap)
        {
            return Result<ModelAnswer>.Failure([overCap]);
        }

        var options = new ChatOptions
        {
            ModelId = profile.Model,
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
            ResponseFormat = request.AnswerSchema is { } schema ? ChatResponseFormat.ForJsonSchema(schema, "answer") : null,
        };

        // Providers that can hold an answer to its schema, rather than try to, are asked to.
        if (request.AnswerSchema is not null)
        {
            options.AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true };
        }

        try
        {
            var response = await client.GetResponseAsync(prompt.Messages, options, cancellationToken);
            var call = client.Recorded!;
            return Result<ModelAnswer>.Success(new ModelAnswer(call.Id, profile.Provider, call.ModelVersion!, response.Text, prompt.Hash, call.Cost));
        }
        catch (Exception) when (client.Recorded is { Outcome: ModelCallOutcome.Failed } failed && !cancellationToken.IsCancellationRequested)
        {
            // The provider's error can quote its request or hold details for an administrator, so the caller gets the record's id.
            return Result<ModelAnswer>.Failure(
                ModelErrorCodes.CallFailed,
                $"The call to the '{profile.Name}' model failed; model call {failed.Id} records the provider's error.");
        }
    }

    private Error? Refusal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (profiles.Find(name) is not { } profile)
        {
            return new Error(
                ModelErrorCodes.NotConfigured,
                $"No model is configured for the profile '{name}'; an administrator sets Models__Profiles__{name}__Provider and __Model.");
        }

        return outbound.IsAllowed(profile.Endpoint)
            ? null
            : new Error(
                ModelErrorCodes.EndpointNotAllowed,
                $"The '{name}' model's endpoint {profile.Endpoint.GetLeftPart(UriPartial.Authority)} is not on the outbound allow-list; "
                + "an administrator adds its host to Outbound__AllowedHosts.");
    }

    // Records the call as pending at the most it can cost, after checking that the charged agent can afford that much (SEC-04).
    // Fails closed: a credential whose caps cannot be read spends nothing.
    private async Task<Error?> ReserveAsync(RecordingChatClient client, Guid? chargedTo, CancellationToken cancellationToken)
    {
        if (chargedTo is not { } agent)
        {
            await client.ReserveAsync(cancellationToken);
            return null;
        }

        var credential = await agents.GetAsync(agent, cancellationToken);
        if (!credential.IsSuccess)
        {
            return new Error(ModelErrorCodes.SpendUnknown, $"The agent credential {agent} could not be read, so its model spend cannot be checked.");
        }

        var caps = credential.Value.Caps;
        var gate = SpendLocks.GetOrAdd(agent, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var spent = await log.SpendAsync(agent, clock.GetUtcNow() - caps.LlmSpendPeriod, cancellationToken);
            if (spent + client.Reserved > caps.LlmSpendPerPeriod)
            {
                return new Error(
                    ModelErrorCodes.SpendCapExceeded,
                    $"The credential '{credential.Value.DisplayName}' has spent {Dollars(spent)} of its {Dollars(caps.LlmSpendPerPeriod)} model spend "
                    + $"in the last {caps.LlmSpendPeriod}, and this call can cost up to {Dollars(client.Reserved)}, so it is not made; "
                    + "an administrator can raise the cap.");
            }

            await client.ReserveAsync(cancellationToken);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Dollars(decimal amount)
    {
        return "$" + amount.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
