namespace FluentCMS.Web.Api.Models;

public class SmtpServerConfiguration
{
    public string Server { get; set; } = default!;
    public int Port { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public bool EnableSsl { get; set; }
    public string From { get; set; } = default!;
}
