using FluentCMS.Content;

namespace FluentCMS.Core.Storage.Entities;

// A row of cms_idempotency_key: one Idempotency-Key of one principal in one site, and the response of the request that
// first used it. A row without a response is a request still running, held by Owner until LeaseUntil.
public sealed class IdempotencyRow
{
    public Guid Id { get; set; }

    public Guid SiteId { get; set; }

    public PrincipalKind PrincipalKind { get; set; }

    public Guid PrincipalId { get; set; }

    // SHA-256 of the key, in hex, so the unique index compares it exactly whatever the column's collation.
    public string KeyHash { get; set; } = "";

    public string RequestHash { get; set; } = "";

    public Guid Owner { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LeaseUntil { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public int? ResponseStatus { get; set; }

    // JSON text of an object of strings: the content type and the headers the response set.
    public string? ResponseHeaders { get; set; }

    public string? ResponseBody { get; set; }
}
