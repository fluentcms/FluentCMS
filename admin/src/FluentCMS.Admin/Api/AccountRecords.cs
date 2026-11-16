namespace FluentCMS.Admin.Api;

// What the account page reads from /me (spec 4.3). They are the admin's own (AQ6) and only the account page reads them, so
// they stay internal rather than join the records of FluentCMS.Admin.Abstractions.

// One authorization of the user: a sign-in through a client, with the refresh tokens that came from it.
internal sealed record MySession
{
    public required Guid Id { get; init; }

    public string? ClientId { get; init; }

    public string? ClientName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];
}

internal sealed record MyTwoFactor
{
    public bool Enabled { get; init; }

    // A role of the user requires two-factor sign-in.
    public bool Required { get; init; }

    // A key was enrolled and waits for a code of it.
    public bool Pending { get; init; }

    public int RecoveryCodesLeft { get; init; }
}

// Shown once, on the step of the enrolment, and never kept.
internal sealed record AuthenticatorSetup
{
    public required string SharedKey { get; init; }

    public required string AuthenticatorUri { get; init; }
}

// Shown once, after a verify or a regeneration, and never kept.
internal sealed record RecoveryCodeList
{
    public IReadOnlyList<string> RecoveryCodes { get; init; } = [];
}

internal sealed record MyLogin
{
    public required string Provider { get; init; }

    public required string ProviderKey { get; init; }

    public string? DisplayName { get; init; }
}

// A null member is one the user has not chosen. A write replaces them all.
internal sealed record MyPreferences
{
    public string? Culture { get; init; }

    public string? TimeZone { get; init; }

    public string? DateFormat { get; init; }

    public string? NumberFormat { get; init; }
}
