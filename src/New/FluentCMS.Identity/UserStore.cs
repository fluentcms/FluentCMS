namespace FluentCMS.Identity;

public partial class UserStore(IUserRepository repository) : IProtectedUserStore<User>
{

}
