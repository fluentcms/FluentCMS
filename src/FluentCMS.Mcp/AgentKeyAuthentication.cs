using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using FluentCMS.Core.Agents;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Mcp;

public static class AgentKeyAuthentication
{
    public const string Scheme = "AgentKey";

    public static AuthenticationBuilder AddFluentCmsAgentKeys(this AuthenticationBuilder builder)
    {
        return builder.AddScheme<AuthenticationSchemeOptions, AgentKeyAuthenticationHandler>(Scheme, displayName: null, configureOptions: null);
    }
}

// The key is read from the Authorization header alone, never from the query string or the body, so it stays out of URLs and the
// logs that record them (SEC-06). Requests are stateless, so every one is checked and a revoked key is refused from its next request.
internal sealed class AgentKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAgentCredentialDirectory directory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string Bearer = "Bearer";
    private const string ErrorCode = "error_code";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization.ToString(), out var header)
            || !Bearer.Equals(header.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var credential = await directory.AuthenticateAsync(header.Parameter ?? string.Empty, Context.RequestAborted);
        if (!credential.IsSuccess)
        {
            var error = credential.Errors[0];
            return AuthenticateResult.Fail(error.Message, new AuthenticationProperties(new Dictionary<string, string?> { [ErrorCode] = error.Code }));
        }

        return AuthenticateResult.Success(new AuthenticationTicket(AgentPrincipal.For(credential.Value, Scheme.Name), Scheme.Name));
    }

    // RFC 6750: a missing key gets a bare challenge, a refused one says why. The header is ASCII and fixed, so the message that
    // names the credential goes in the body, where an agent or the person running it reads what to do.
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var result = await HandleAuthenticateOnceSafeAsync();
        var code = result.Properties?.GetString(ErrorCode);

        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = code is null ? Bearer : $"{Bearer} error=\"invalid_token\", error_description=\"{Description(code)}\"";
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.WriteAsync(
            result.Failure?.Message
                ?? "This endpoint needs an agent key, sent as \"Authorization: Bearer <key>\". An administrator issues keys on /agents/new.",
            Context.RequestAborted);
    }

    private static string Description(string code) => code switch
    {
        AgentCredentialErrorCodes.Expired => "The key has expired; ask an administrator for a new one.",
        AgentCredentialErrorCodes.Revoked => "The key has been revoked; ask an administrator for a new one.",
        _ => "The key is not a valid agent key.",
    };
}
