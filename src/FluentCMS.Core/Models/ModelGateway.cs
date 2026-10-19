using System.Globalization;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using Microsoft.Extensions.AI;

namespace FluentCMS.Core.Models;

// The checks that decide whether a call may go out come first and return results; the call itself goes through the recording
// middleware, so a call that is sent is always on record.
internal sealed class ModelGateway(
    IModelProfiles profiles,
    IModelClients clients,
    IModelCallLog log,
    IOutboundHosts outbound,
    IAgentCredentialDirectory agents,
    ICurrentInitiator currentInitiator,
    TimeProvider clock) : IModelGateway
{
    public bool IsAvailable(string profile)
    {
        return Refusal(profile) is null;
    }

    public async Task<Result<ModelAnswer>> AskAsync(ModelRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Purpose);

        if (Refusal(request.Profile) is { } refused)
        {
            return Result<ModelAnswer>.Failure([refused]);
        }

        var profile = profiles.Find(request.Profile)!;
        var initiator = currentInitiator.Current;
        var chargedTo = request.ChargedTo ?? (initiator as AgentInitiator)?.CredentialId;
        if (chargedTo is { } agent && await SpendRefusalAsync(agent, cancellationToken) is { } overCap)
        {
            return Result<ModelAnswer>.Failure([overCap]);
        }

        var prompt = ModelPrompt.Build(request.Instructions, request.Data);
        // Not disposed: disposing it would dispose the provider client, which the wiring keeps for the profile's next call.
        var client = new RecordingChatClient(clients.Create(profile), log, profile, request.Purpose, prompt, initiator, chargedTo, clock);
        var options = new ChatOptions
        {
            ModelId = profile.Model,
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
            ResponseFormat = request.AnswerSchema is { } schema ? ChatResponseFormat.ForJsonSchema(schema, "answer") : null,
        };

        try
        {
            var response = await client.GetResponseAsync(prompt.Messages, options, cancellationToken);
            var call = client.Recorded!;
            return Result<ModelAnswer>.Success(new ModelAnswer(call.Id, profile.Provider, call.ModelVersion!, response.Text, prompt.Hash, call.Cost));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return Result<ModelAnswer>.Failure(
                ModelErrorCodes.CallFailed,
                $"The call to the '{profile.Name}' model failed, and it is on record: {exception.Message}");
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

        return outbound.IsAllowed(profile.Endpoint.IdnHost)
            ? null
            : new Error(
                ModelErrorCodes.EndpointNotAllowed,
                $"The '{name}' model's endpoint host {profile.Endpoint.IdnHost} is not on the outbound allow-list; an administrator adds it to Outbound__AllowedHosts.");
    }

    // Fails closed: a credential whose caps cannot be read spends nothing (SEC-04).
    private async Task<Error?> SpendRefusalAsync(Guid agent, CancellationToken cancellationToken)
    {
        var credential = await agents.GetAsync(agent, cancellationToken);
        if (!credential.IsSuccess)
        {
            return new Error(ModelErrorCodes.SpendUnknown, $"The agent credential {agent} could not be read, so its model spend cannot be checked.");
        }

        var caps = credential.Value.Caps;
        var spent = await log.SpendAsync(agent, clock.GetUtcNow() - caps.LlmSpendPeriod, cancellationToken);
        if (spent < caps.LlmSpendPerPeriod)
        {
            return null;
        }

        return new Error(
            ModelErrorCodes.SpendCapExceeded,
            $"The credential '{credential.Value.DisplayName}' has spent {Dollars(spent)} of its {Dollars(caps.LlmSpendPerPeriod)} model spend "
            + $"in the last {caps.LlmSpendPeriod}, so no model call is made for it; an administrator can raise the cap.");
    }

    private static string Dollars(decimal amount)
    {
        return "$" + amount.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
