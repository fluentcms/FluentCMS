namespace FluentCMS.Services;

public class PagePolicies
{
    public List<string> SuperAdmin { get; }
    public List<string> Admin { get; }
    public List<string> Editor { get; }

    public PagePolicies()
    {
        SuperAdmin = [Policies.SUPER_ADMIN];
        Admin = [Policies.SUPER_ADMIN, Policies.SITE_ADMIN];
        Editor = [Policies.SUPER_ADMIN, Policies.SITE_ADMIN, Policies.SITE_EDITOR];
    }
}
