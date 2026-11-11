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

    // The pages of the member pool, which have a sign-in cookie of their own.
    public const string MemberLogin = "/account/members/login";
    public const string MemberLogout = "/account/members/logout";
    public const string MemberRegister = "/account/members/register";
    public const string MemberVerifyEmail = "/account/members/verify-email";
    public const string MemberForgotPassword = "/account/members/forgot-password";
    public const string MemberResetPassword = "/account/members/reset-password";
    public const string MemberExternalLogin = "/account/members/external-login";
    public const string MemberLogins = "/account/members/logins";
}
