namespace FluentCMS.Entities;

public class User : AuditEntity
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }
}
