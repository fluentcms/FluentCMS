namespace FluentCMS.Api.Models.Hosts;

public class UpdateHostRequest
{
    public List<Guid> SuperUserIds { get; set; } = [];
}
