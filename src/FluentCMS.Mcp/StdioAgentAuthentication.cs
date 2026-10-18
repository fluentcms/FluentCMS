using System.Security.Claims;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Identity;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FluentCMS.Mcp;

// stdio has no headers, so the agent's key comes from the environment of the process it starts; a command-line argument would
// show the key in process listings. The key is checked again on every message, so one revoked or expired during a session is
// refused from its next message on, as over HTTP.
public static class StdioAgentAuthentication
{
    public const string KeyVariable = "FLUENTCMS_AGENT_KEY";

    internal static McpMessageFilter Filter(string? key) => next => async (context, cancellationToken) =>
    {
        // Responses and errors answer the server's own requests and run nothing, so only requests and notifications are checked.
        if (context.JsonRpcMessage is not (JsonRpcRequest or JsonRpcNotification))
        {
            await next(context, cancellationToken);
            return;
        }

        var refusal = string.IsNullOrEmpty(key)
            ? $"No agent key was given. Set the {KeyVariable} environment variable of this process to a key an administrator issues on /agents/new."
            : null;
        ClaimsPrincipal? agent = null;

        if (refusal is null)
        {
            await using var scope = context.Server.Services!.CreateAsyncScope();
            var credential = await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().AuthenticateAsync(key!, cancellationToken);
            if (credential.IsSuccess)
            {
                agent = AgentPrincipal.For(credential.Value, AgentKeyAuthentication.Scheme);
            }
            else
            {
                refusal = credential.Errors[0].Message;
            }
        }

        if (agent is null)
        {
            // A notification gets no answer, so a refused one is dropped.
            if (context.JsonRpcMessage is JsonRpcRequest)
            {
                throw new McpProtocolException(refusal!, McpErrorCode.InvalidRequest);
            }

            return;
        }

        context.User = agent;
        McpMessageInitiator.Agent = agent;
        await next(context, cancellationToken);
    };
}

// The agent of the stdio message being handled, which the filter above accepted; null outside a message, such as in a job.
internal sealed class McpMessageInitiator : ICurrentInitiator
{
    private static readonly AsyncLocal<ClaimsPrincipal?> Caller = new();

    public static ClaimsPrincipal? Agent
    {
        get => Caller.Value;
        set => Caller.Value = value;
    }

    public Initiator? Current => ClaimsInitiator.From(Caller.Value);
}
