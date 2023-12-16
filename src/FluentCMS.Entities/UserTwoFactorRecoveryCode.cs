namespace FluentCMS.Entities;

/// <summary>
/// Represents a two-factor authentication recovery code.
/// </summary>
public class UserTwoFactorRecoveryCode
{
    /// <summary>
    /// Gets or sets the recovery code.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the recovery code has been redeemed.
    /// </summary>
    public bool Redeemed { get; set; }
}
