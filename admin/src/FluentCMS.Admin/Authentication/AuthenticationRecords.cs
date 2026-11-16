using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Authentication;

// The members of the issuer's discovery document the admin uses.
internal sealed record OpenIdConfiguration
{
    public string? Issuer { get; init; }

    public Uri? AuthorizationEndpoint { get; init; }

    public Uri? TokenEndpoint { get; init; }

    public Uri? RevocationEndpoint { get; init; }

    public Uri? EndSessionEndpoint { get; init; }

    public IReadOnlyList<string>? CodeChallengeMethodsSupported { get; init; }
}

// A successful answer of the token endpoint.
internal sealed record TokenAnswer
{
    public string? AccessToken { get; init; }

    public string? TokenType { get; init; }

    public int? ExpiresIn { get; init; }

    public string? RefreshToken { get; init; }

    public string? IdToken { get; init; }
}

// An error answer of the token endpoint (RFC 6749, 5.2).
internal sealed record TokenError
{
    public string? Error { get; init; }
}

// The user the id token of the sign-in named. Not secret, so it is kept beside the refresh token for a reload of the tab.
internal sealed record SignedInUser(string Subject, string? Name, string? Email);

// What the tab keeps of a session across a reload: the refresh token and the user. The access token stays in memory.
internal sealed record StoredSession(string RefreshToken, SignedInUser User);

// A sign-in that left the tab for the authorize endpoint: the secrets that bind the callback to it, used once, and the
// admin route to return to.
internal sealed record PendingSignIn(string State, string Nonce, string Verifier, string ReturnRoute, DateTimeOffset StartedAt);

// A draft as the tab keeps it, under the user it belongs to.
internal sealed record StoredDraft(string User, string? Site, string Type, Guid? Entry, string? Locale, string BaseVersion, JsonObject Fields);
