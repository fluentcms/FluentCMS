namespace FluentCMS.Core.Identity;

// A row of cms_invitation. The token is only in the email; the row keeps its SHA-256, so the table cannot be read for one.
internal sealed class Invitation
{
    public Guid Id { get; set; }

    public string Email { get; set; } = "";

    public string NormalizedEmail { get; set; } = "";

    public string TokenHash { get; set; } = "";

    // JSON array of the ids of the roles the user gets on accepting.
    public string RoleIds { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    // The user the invitation became.
    public Guid? UserId { get; set; }

    // A new invitation to the same address, or a failed send, ends the old one.
    public DateTimeOffset? RevokedAt { get; set; }
}
