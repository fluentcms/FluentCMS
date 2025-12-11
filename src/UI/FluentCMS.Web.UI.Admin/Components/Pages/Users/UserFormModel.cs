namespace FluentCMS.Web.UI.Admin;

public class UserFormModel
{
    public Guid? Id { get; set; }
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? Description { get; set; }

    public bool Suspended { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool Locked { get; set; }
}
