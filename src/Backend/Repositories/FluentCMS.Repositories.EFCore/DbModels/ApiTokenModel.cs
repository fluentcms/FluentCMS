namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ApiTokens")]
public class ApiTokenModel : AuditableEntityModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Key { get; set; } = default!;
    public string Secret { get; set; } = default!;
    public DateTime? ExpireAt { get; set; }
    public bool Enabled { get; set; } = true;
    public ICollection<ApiTokenPolicyModel> Policies { get; set; } = [];
}
