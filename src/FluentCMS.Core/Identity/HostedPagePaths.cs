namespace FluentCMS.Core.Identity;

// The routes of the hosted pages, relative to the path base. The @page directives repeat them.
internal static class HostedPagePaths
{
    public const string Login = "/account/login";
    public const string Logout = "/account/logout";
    public const string Consent = "/account/consent";
    public const string TwoFactor = "/account/two-factor";
    public const string RecoveryCode = "/account/recovery-code";
    public const string EnableTwoFactor = "/account/enable-two-factor";
    public const string ForgotPassword = "/account/forgot-password";
    public const string ResetPassword = "/account/reset-password";
    public const string AcceptInvitation = "/account/accept-invitation";
    public const string ChangePassword = "/account/change-password";
    public const string ExternalLogin = "/account/external-login";
    public const string Error = "/account/error";
}
