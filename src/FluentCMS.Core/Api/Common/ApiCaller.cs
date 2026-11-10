using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Core.Api.Common;

// Who a request comes from, as the rate limit policies and the Delivery output cache ask it, read once per request from
// the principal the FluentCms scheme settled: ApiAuthenticationMiddleware settles it, and a route that middleware skips
// reads the request's user the first time it is asked, which is after authentication. The client address is read once
// too, before authentication, by the limit that runs there.
internal sealed class ApiCaller
{
    private static readonly object AddressItem = new();

    private readonly ClaimsPrincipal _principal;
    private string? _cacheKey;

    private ApiCaller(ClaimsPrincipal principal)
    {
        _principal = principal;
        string? kind = null, subject = null, rateLimit = null;
        foreach (var claim in principal.Claims)
        {
            switch (claim.Type)
            {
                case TokenScopes.Claim:
                    Manages |= claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(TokenScopes.Management, StringComparer.Ordinal);
                    break;
                case CmsClaimTypes.Kind:
                    kind ??= claim.Value;
                    break;
                case CmsClaimTypes.Subject:
                    subject ??= claim.Value;
                    break;
                case CmsClaimTypes.RateLimit:
                    rateLimit ??= claim.Value;
                    break;
            }
        }

        IsAuthenticated = principal.Identity?.IsAuthenticated == true;
        IsApiKey = kind == nameof(PrincipalKind.ApiKey);
        Subject = string.IsNullOrEmpty(subject) ? null : subject;
        RateLimit = int.TryParse(rateLimit, NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0 ? limit : null;
    }

    public bool IsAuthenticated { get; }

    public bool IsApiKey { get; }

    // A bearer token or a key with the management scope, which is who the Management API is for.
    public bool Manages { get; }

    public string? Subject { get; }

    // The key's own fcms_rate_limit, when it has a usable one.
    public int? RateLimit { get; }

    // An API key's copy in the Delivery output cache: a hash of all its claims, which name its role, sites, types and scope.
    public string KeyCacheKey => _cacheKey ??= Hash(_principal);

    public static ApiCaller Of(HttpContext context)
    {
        if (context.Features.Get<ApiCaller>() is { } caller)
        {
            return caller;
        }

        caller = new ApiCaller(ApiAuthentication.Of(context));
        context.Features[typeof(ApiCaller)] = caller;
        return caller;
    }

    public static void Settle(HttpContext context) => context.Features[typeof(ApiCaller)] = new ApiCaller(ApiAuthentication.Of(context));

    // The client address as the limits partition by it, shared by the limit before authentication and the endpoint's.
    public static string Address(HttpContext context)
    {
        if (context.Items.TryGetValue(AddressItem, out var known) && known is string address)
        {
            return address;
        }

        address = "ip:" + ClientAddress.Partition(context.Connection.RemoteIpAddress);
        context.Items[AddressItem] = address;
        return address;
    }

    // Each type and value goes in with its length before it, so that no two sets of claims hash the same bytes.
    private static string Hash(ClaimsPrincipal principal)
    {
        var claims = principal.Claims.ToArray();
        Array.Sort(claims, static (left, right) => string.CompareOrdinal(left.Type, right.Type) is var order and not 0 ? order : string.CompareOrdinal(left.Value, right.Value));

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var claim in claims)
        {
            Append(sha, claim.Type);
            Append(sha, claim.Value);
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        sha.GetHashAndReset(digest);
        return string.Create(4 + (digest.Length * 2), digest, static (chars, bytes) =>
        {
            "key:".CopyTo(chars);
            Convert.TryToHexStringLower(bytes, chars[4..], out _);
        });
    }

    private static void Append(IncrementalHash sha, string text)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(int) + length);
        try
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, length);
            Encoding.UTF8.GetBytes(text, buffer.AsSpan(sizeof(int)));
            sha.AppendData(buffer, 0, sizeof(int) + length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
