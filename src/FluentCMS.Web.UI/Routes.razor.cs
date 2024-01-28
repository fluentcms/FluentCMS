namespace FluentCMS.Web.UI;

public partial class Routes
{
    public static string AuthForgotPassword() => "/auth/forgot-password";

    public static string AuthLogin() => "/auth/login";

    public static string AuthLogout() => "/auth/logout";

    public static string AuthRegister() => "/auth/register";

    public static string Home() => "/";

    public static string TermsOfUse() => "/terms-of-use";

    public static string UserDetails(string Id) => string.Format("/user/$0", Id);

    public static string PrivacyPolicy() => "/privacy-policy";
}
