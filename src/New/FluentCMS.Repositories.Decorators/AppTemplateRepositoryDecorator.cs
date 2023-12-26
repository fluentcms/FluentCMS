namespace FluentCMS.Repositories.Decorators;

public class AppTemplateRepositoryDecorator : EntityRepositoryDecorator<AppTemplate>, IAppTemplateRepository
{
    public AppTemplateRepositoryDecorator(IAuthContext authContext, IAppTemplateRepository decorator) : base(authContext, decorator)
    {
    }
}
