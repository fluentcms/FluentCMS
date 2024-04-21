
using System.ComponentModel.DataAnnotations;

namespace FluentCMS.Web.Api.Models;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DomainNameAttribute : RegularExpressionAttribute
{
    const string _validDomainNameRegex = @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])(:\d{1,5})?$";
    public DomainNameAttribute() : base(_validDomainNameRegex)
    {
        ErrorMessage = "Invalid domain name";
    }
}
