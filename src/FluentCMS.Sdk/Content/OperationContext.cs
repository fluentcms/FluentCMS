using FluentCMS.Sites;

namespace FluentCMS.Content;

/// <summary>
/// The ambient, immutable record that flows through every operation. Versions, the activity log,
/// hooks, webhooks and MCP origin tagging all read from it.
/// </summary>
/// <param name="Principal">Who performs the operation.</param>
/// <param name="Origin">Where the operation entered the system.</param>
/// <param name="Site">The site the operation targets.</param>
/// <param name="Locale">The resolved request locale, as a BCP 47 tag.</param>
/// <param name="ClientId">The OAuth client id or API key id, when there is one.</param>
/// <param name="CorrelationId">An id that ties together everything done for one request or job.</param>
/// <param name="Timestamp">When the operation started.</param>
public sealed record OperationContext(
    Principal Principal,
    Origin Origin,
    SiteId Site,
    string Locale,
    string? ClientId,
    string CorrelationId,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Who this operation acts for, when <see cref="Principal"/> does the work on someone's behalf. It is for the
    /// audit trail only: authorization reads <see cref="Principal"/> and never this.
    /// </summary>
    public Principal? Actor { get; init; }

    /// <summary>The identity to record on what this operation writes: <see cref="Actor"/> if there is one, otherwise <see cref="Principal"/>.</summary>
    public Principal AuditPrincipal => Actor ?? Principal;
}
