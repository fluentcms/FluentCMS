namespace FluentCMS.Entities.Identity;

public class TwoFactorRecoveryCode
{
    public string Code { get; set; }
    public bool Redeemed { get; set; }
}
