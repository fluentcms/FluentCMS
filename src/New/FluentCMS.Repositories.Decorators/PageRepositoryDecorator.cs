namespace FluentCMS.Repositories.Decorators;

public class PageRepositoryDecorator : SiteAssociatedRepositoryDecorator<Page>, IPageRepository
{
    public PageRepositoryDecorator(IAuthContext authContext, IPageRepository decorator) : base(authContext, decorator)
    {
    }

}
