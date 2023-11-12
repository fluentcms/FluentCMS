namespace FluentCMS.Api.Models;

public class HostUpdateRequest
{
    public List<Guid> SuperUserIds { get; set; } = [];
}
