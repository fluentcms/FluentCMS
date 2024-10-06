namespace FluentCMS.Web.Api.Models;

public class SettingsUpdateRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public Dictionary<string, string> Settings { get; set; } = [];
}
