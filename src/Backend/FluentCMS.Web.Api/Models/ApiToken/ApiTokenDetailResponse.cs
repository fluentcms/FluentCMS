namespace FluentCMS.Web.Api.Models;

public class ApiTokenDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Token { get; set; } = default!;
    public DateTime? ExpiredAt { get; set; }
    public bool Enabled { get; set; } = true;
    public ICollection<Policy> Policies { get; set; } = [];
    public string? ApiKey { get; set; }
}
