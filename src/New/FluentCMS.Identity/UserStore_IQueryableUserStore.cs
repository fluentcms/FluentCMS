namespace FluentCMS.Identity;

public partial class UserStore : IQueryableUserStore<User>
{
    public IQueryable<User> Users => repository.AsQueryable();
}
