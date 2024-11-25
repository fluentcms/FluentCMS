namespace FluentCMS.Repositories.EFCore.DbModels;

[Table("ApiTokenPolicies")]
public class ApiTokenPolicyModel : EntityModel
{
    public Guid ApiTokenId { get; set; } // foreign key
    public string Area { get; set; } = default!;
    public string Actions { get; set; } = default!; // comma separated list of actions
    public ApiTokenModel ApiToken { get; set; } = default!; // navigation property
}
