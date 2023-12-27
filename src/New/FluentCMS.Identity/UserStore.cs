using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Identity;

public partial class UserStore(IUserRepository repository) : IProtectedUserStore<User>
{

}
