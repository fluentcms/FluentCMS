using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api;

public class AnyPolicyAttribute : PolicyAttribute, IAllowAnonymous
{
    public const string AREA = "Global";
    public const string ACTION = "All";

    public AnyPolicyAttribute() : base(AREA, ACTION)
    {
    }
}
