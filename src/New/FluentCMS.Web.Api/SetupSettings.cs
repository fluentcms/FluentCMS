namespace FluentCMS.Web.Api;
public class SetupSettings
{
    public User SuperAdmin { get; set; } = default!;

    public class User
    {
        public string Username { get; set; } = default!;
        public string Email { get; set; } = default!;
        public string Password { get; set; } = default!;
    }
}
