namespace FluentCMS.Web.Api.Models;

public class SettingsDetailResponse
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public Dictionary<string, string> Settings { get; set; } = [];
}
