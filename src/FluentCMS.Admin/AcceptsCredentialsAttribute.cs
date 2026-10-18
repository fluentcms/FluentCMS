namespace FluentCMS.Admin;

// Marks a page whose form takes a password; the host rate limits posts to it (SEC-07).
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AcceptsCredentialsAttribute : Attribute;
