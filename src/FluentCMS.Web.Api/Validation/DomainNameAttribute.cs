namespace FluentCMS.Web.Api.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DomainNameAttribute : RegularExpressionAttribute
{
    public const string VALID_DOMAIN_NAME_REGEX = @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])(:\d{1,5})?$";

    public DomainNameAttribute() : base(VALID_DOMAIN_NAME_REGEX)
    {
        ErrorMessage = "Invalid domain name";
    }
}
