using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Delivery;

// Preview tokens for change sets (API-04). Only a person issues or revokes one, so unapproved content never leaves through an agent;
// each write appends its audit event in the same unit of work (AUD-01).
public interface IPreviewTokens
{
    // The token is in the result once and never stored; only its hash is.
    Task<Result<IssuedPreviewToken>> IssueAsync(Guid changeSetId, CancellationToken cancellationToken = default);

    Task<Result> RevokeAsync(Guid tokenId, CancellationToken cancellationToken = default);

    // Newest first, without their secrets.
    Task<Result<IReadOnlyList<PreviewToken>>> ListAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}

// Reads with a preview token: entries as the token's change set would publish them. A token that is unknown, malformed, expired,
// revoked, or whose set has been published, rejected or reverted fails alike, so its holder learns nothing about why.
public interface IPreviewContent
{
    Task<Result<PublishedItem>> GetAsync(string token, Guid entryId, CancellationToken cancellationToken = default);

    // Every entry the set creates or updates, in the order of its operations.
    Task<Result<PreviewChanges>> ChangesAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record IssuedPreviewToken(Guid Id, Guid ChangeSetId, string Token, DateTimeOffset ExpiresAt);

public sealed record PreviewToken(Guid Id, Guid ChangeSetId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt);

public sealed record PreviewChanges(Guid ChangeSetId, IReadOnlyList<PublishedItem> Items);

// A token is 32 random bytes, so its SHA-256 is stored and looked up directly, as for agent keys (SEC-01). The prefix lets people
// and secret scanners recognise one.
public static class PreviewTokenKeys
{
    public const string Prefix = "fcmsp_";

    private const int SecretBytes = 32;

    private static readonly SearchValues<char> Alphabet =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_");

    public static int Length { get; } = Prefix.Length + Base64Url.GetEncodedLength(SecretBytes);

    // The states in which a set's content can still change or publish; a token works only while its set is in one.
    public static IReadOnlyList<ChangeSetState> Previewable { get; } =
        [ChangeSetState.Open, ChangeSetState.ChecksRunning, ChangeSetState.ChecksFailed, ChangeSetState.AwaitingReview, ChangeSetState.Approved];

    public static string Generate()
    {
        return Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    // The alphabet is checked first, because the base64url check skips whitespace.
    public static bool IsWellFormed(string? token)
    {
        return token is not null
            && token.Length == Length
            && token.StartsWith(Prefix, StringComparison.Ordinal)
            && !token.AsSpan(Prefix.Length).ContainsAnyExcept(Alphabet)
            && Base64Url.IsValid(token.AsSpan(Prefix.Length));
    }

    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
