namespace FluentCMS.Infrastructure.Delivery;

// A preview token of a change set (API-04). Only the token's SHA-256 is stored, so a stolen table reveals no token.
public sealed class PreviewTokenRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public Guid ChangeSetId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
