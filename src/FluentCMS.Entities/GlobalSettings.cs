namespace FluentCMS.Entities;

public class GlobalSettings : AuditableEntity
{
    public List<string> SuperUsers { get; set; } = [];
    public SmtpServerConfiguration Email { get; set; } = default!;
}

public class SmtpServerConfiguration
{
    public string Server { get; set; } = default!;
    public int Port { get; set; } = default!;
    public string Username { get; set; } = default!;
    public string Password { get; set; } = default!;
    public bool EnableSsl { get; set; }
    public string From { get; set; } = default!;
}
