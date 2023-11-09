namespace FluentCMS.Api.Models.Hosts;

public class HostResponse
{
    public Guid Id { get; set; } = default!;

    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }

    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }

    public List<Guid> SuperUserIds { get; set; } = [];
}
