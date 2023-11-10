//TODO: where should we put this?
namespace FluentCMS.Services;

public static class ForgotPasswordMessage
{
    public static string Subject { get; set; } = "Forgot Password";
    public static string Body { get; set; } = "Your reset password token is {token}"; // {token} is filled with reset password token

    public static string GetBodyWithReplaces(string token) => Body.Replace("{token}", token);
}