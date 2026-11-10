using System.Globalization;
using System.Net;
using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Content;
using FluentCMS.Core.Api.Common;
using FluentCMS.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace FluentCMS.Core.Identity.Oidc;

// POST /connect/register, dynamic client registration (RFC 7591) for MCP clients. It exists while a registration policy
// does, which the MCP plugin brings, and that policy is not Off; otherwise it is a 404. Anyone may call it, so everything a
// client sends is held to what a public MCP client needs: no secret, the code flow with refresh tokens, redirect URIs on
// https or on loopback, and the scopes of AuthorizationServer.RegisteredClientScopes. Whatever else it sends is ignored and
// not echoed. A refusal of the metadata is the error body of RFC 7591; the limits answer as the limits of core do.
internal static class DynamicClientRegistration
{
    public const string Path = "/connect/register";

    public const int MaxBodyBytes = 16 * 1024;
    public const int MaxRedirectUris = 10;
    public const int MaxRedirectUriLength = 2048;
    public const int MaxNameLength = 128;
    public const string DefaultName = "MCP client";

    private const string InvalidRedirectUri = "invalid_redirect_uri";
    private const string InvalidClientMetadata = "invalid_client_metadata";

    private static readonly string[] AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];

    public static async Task<IResult> RegisterAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var policy = services.GetService<IMcpClientRegistrationPolicy>() is { } source ? await source.GetAsync(context.RequestAborted) : null;
        if (policy is null || policy.Mode == McpClientRegistrationMode.Off)
        {
            await ProblemWriter.WriteAsync(context, ProblemMapper.Known(ApiErrorCodes.NotFound, "Clients do not register themselves at this server."), cancellationToken: context.RequestAborted);
            return Results.Empty;
        }

        context.Response.Headers.CacheControl = "no-store";
        if (await ReadAsync(context) is not { } body)
        {
            return Refuse(InvalidClientMetadata, $"The registration is a JSON object of at most {MaxBodyBytes} bytes, sent as application/json.");
        }

        using (body)
        {
            var (metadata, error, description) = Parse(body.RootElement);
            if (metadata is null)
            {
                return Refuse(error!, description!);
            }

            // After the metadata is checked, so that a refused registration spends nothing of the address's hour.
            if (!services.GetRequiredService<ClientRegistrationLimiter>().TryAcquire(ApiCaller.Address(context), policy.RegistrationsPerHour, out var retryAfter))
            {
                var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                context.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await ProblemWriter.WriteAsync(context, ProblemMapper.Known(LimitErrorCodes.RateLimited, $"Too many clients were registered from this address; try again in {seconds} seconds."), cancellationToken: context.RequestAborted);
                return Results.Empty;
            }

            var address = context.Connection.RemoteIpAddress is { } remote ? (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString() : null;
            var registered = await services.GetRequiredService<McpClients>().RegisterAsync(metadata, policy, Source(context), address, context.RequestAborted);
            if (registered is null)
            {
                OidcLog.ClientRegistrationCapped(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DynamicClientRegistration)), policy.MaxPendingClients);
                context.Response.Headers.RetryAfter = "3600";
                return Results.Json(
                    new Dictionary<string, object?>
                    {
                        [Parameters.Error] = Errors.TemporarilyUnavailable,
                        [Parameters.ErrorDescription] = "Too many clients wait for approval; try again once an administrator has gone through them.",
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // What was registered, which is what the client sent where it sent it, and the defaults where it did not.
            return Results.Json(
                new Dictionary<string, object?>
                {
                    ["client_id"] = registered.ClientId,
                    ["client_id_issued_at"] = registered.IssuedAt.ToUnixTimeSeconds(),
                    ["client_name"] = metadata.Name,
                    ["redirect_uris"] = metadata.RedirectUris.Select(uri => uri.OriginalString).ToArray(),
                    ["grant_types"] = metadata.GrantTypes,
                    ["response_types"] = new[] { ResponseTypes.Code },
                    ["token_endpoint_auth_method"] = ClientAuthenticationMethods.None,
                    ["scope"] = string.Join(' ', metadata.Scopes),
                },
                statusCode: StatusCodes.Status201Created);
        }
    }

    // The metadata of a registration, or the error of RFC 7591 that refuses it. A member of the wrong JSON type is refused,
    // and a member this server has no use for is ignored.
    internal static (ClientMetadata? Metadata, string? Error, string? Description) Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return Invalid(InvalidClientMetadata, "The registration is not a JSON object.");
        }

        if (!body.TryGetProperty("redirect_uris", out var uris) || uris.ValueKind != JsonValueKind.Array || uris.GetArrayLength() is 0 or > MaxRedirectUris)
        {
            return Invalid(InvalidRedirectUri, $"redirect_uris is a list of 1 to {MaxRedirectUris} URIs.");
        }

        List<Uri> redirectUris = [];
        foreach (var item in uris.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || RedirectUri(item.GetString()!) is not { } uri)
            {
                return Invalid(InvalidRedirectUri, "A redirect URI is an absolute https URI, or an http URI on localhost, 127.0.0.1 or [::1], with no wildcard, fragment or user information.");
            }

            if (!redirectUris.Exists(known => known.OriginalString == uri.OriginalString))
            {
                redirectUris.Add(uri);
            }
        }

        // RFC 7591 reads a missing method as client_secret_basic. This server gives no secret, so a missing method is
        // registered as none, which the answer says, and a client that asks for anything else is refused.
        if (body.TryGetProperty("token_endpoint_auth_method", out var method)
            && (method.ValueKind != JsonValueKind.String || method.GetString() != ClientAuthenticationMethods.None))
        {
            return Invalid(InvalidClientMetadata, "token_endpoint_auth_method is none: a client that registers itself is public and gets no secret.");
        }

        List<string> grantTypes = [.. AllowedGrantTypes];
        if (body.TryGetProperty("grant_types", out var grants))
        {
            if (Strings(grants) is not { } asked
                || asked.Exists(grant => !AllowedGrantTypes.Contains(grant, StringComparer.Ordinal))
                || !asked.Contains(GrantTypes.AuthorizationCode, StringComparer.Ordinal))
            {
                return Invalid(InvalidClientMetadata, "grant_types holds authorization_code, and refresh_token when the client refreshes; no other grant is given.");
            }

            grantTypes = [.. AllowedGrantTypes.Where(grant => asked.Contains(grant, StringComparer.Ordinal))];
        }

        if (body.TryGetProperty("response_types", out var responses)
            && (Strings(responses) is not { Count: > 0 } types || types.Exists(type => type != ResponseTypes.Code)))
        {
            return Invalid(InvalidClientMetadata, "response_types holds code alone.");
        }

        List<string> scopes = [.. AuthorizationServer.RegisteredClientScopes];
        if (body.TryGetProperty("scope", out var scope))
        {
            if (scope.ValueKind != JsonValueKind.String)
            {
                return Invalid(InvalidClientMetadata, "scope is a string of scopes separated by spaces.");
            }

            var asked = scope.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
            if (asked.Find(name => !AuthorizationServer.RegisteredClientScopes.Contains(name, StringComparer.Ordinal)) is { } refused)
            {
                return Invalid(InvalidClientMetadata, $"The scope '{Shown(refused)}' is not given to a client that registers itself; the scopes are {string.Join(", ", AuthorizationServer.RegisteredClientScopes)}.");
            }

            if (asked.Count > 0)
            {
                scopes = [.. AuthorizationServer.RegisteredClientScopes.Where(name => asked.Contains(name, StringComparer.Ordinal))];
            }
        }

        var name = DefaultName;
        if (body.TryGetProperty("client_name", out var named))
        {
            if (named.ValueKind != JsonValueKind.String
                || named.GetString()!.Trim() is not { Length: > 0 and <= MaxNameLength } given
                || given.Any(char.IsControl))
            {
                return Invalid(InvalidClientMetadata, $"client_name is 1 to {MaxNameLength} characters with no control character.");
            }

            name = given;
        }

        return (new ClientMetadata(name, redirectUris, grantTypes, scopes), null, null);
    }

    // https, or http on loopback, where a native client listens. A wildcard would match hosts or paths the client did not
    // name, a fragment is not sent to the server, and user information is a way to dress one host up as another.
    internal static Uri? RedirectUri(string value)
    {
        if (value.Length is 0 or > MaxRedirectUriLength
            || value.Contains('*', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal)
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.IsWellFormedOriginalString()
            || uri.Host.Length == 0
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0)
        {
            return null;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && IsLoopback(uri)) ? uri : null;
    }

    private static bool IsLoopback(Uri uri) =>
        string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
            && IPAddress.IsLoopback(address));

    private static List<string>? Strings(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array && element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
            ? [.. element.EnumerateArray().Select(item => item.GetString()!)]
            : null;

    // A value a caller sent, short and without control characters, for an error message.
    private static string Shown(string value) =>
        new([.. (value.Length > 64 ? value[..64] : value).Where(character => !char.IsControl(character))]);

    private static (ClientMetadata? Metadata, string? Error, string? Description) Invalid(string error, string description) => (null, error, description);

    private static async Task<JsonDocument?> ReadAsync(HttpContext context)
    {
        if (!context.Request.HasJsonContentType() || context.Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        var buffer = new byte[MaxBodyBytes + 1];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await context.Request.Body.ReadAsync(buffer.AsMemory(read), context.RequestAborted);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        if (read > MaxBodyBytes)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(buffer.AsMemory(0, read), new JsonDocumentOptions { MaxDepth = 8 });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The registration is the anonymous principal's, of no site, and comes in for an MCP client.
    private static EventSource Source(HttpContext context) =>
        new(
            context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            Guid.Empty,
            Principal.Anonymous.Id,
            Principal.Anonymous.Name,
            PrincipalKind.Anonymous,
            Origin.Mcp,
            System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier);

    private static IResult Refuse(string error, string description) =>
        Results.Json(
            new Dictionary<string, object?> { [Parameters.Error] = error, [Parameters.ErrorDescription] = description },
            statusCode: StatusCodes.Status400BadRequest);
}

// How many clients one client address registered in the last hour, on this instance. Open registration lets anyone make
// a client that works at once, and a pending one fills a place of the cap, so an address gets a few an hour, far below
// what the auth limit of the group lets through.
internal sealed class ClientRegistrationLimiter(TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private const int SweepAbove = 4096;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _registered = new(StringComparer.Ordinal);

    public bool TryAcquire(string address, int limit, out TimeSpan retryAfter)
    {
        var now = time.GetUtcNow();
        lock (_gate)
        {
            if (_registered.Count > SweepAbove)
            {
                foreach (var idle in _registered.Where(entry => entry.Value.All(at => now - at >= Window)).Select(entry => entry.Key).ToList())
                {
                    _registered.Remove(idle);
                }
            }

            if (!_registered.TryGetValue(address, out var times))
            {
                _registered[address] = times = new Queue<DateTimeOffset>();
            }

            while (times.TryPeek(out var oldest) && now - oldest >= Window)
            {
                times.Dequeue();
            }

            if (times.Count >= Math.Max(1, limit))
            {
                retryAfter = Window - (now - times.Peek());
                return false;
            }

            times.Enqueue(now);
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }
}
