using Microsoft.AspNetCore.Authorization;

namespace FluentCMS.Web.Api;

public class PolicyAllAttribute : PolicyAttribute, IAllowAnonymous
{
    public const string AREA = "Global";
    public const string ACTION = "All";

    public PolicyAllAttribute() : base(AREA, ACTION)
    {
    }
}