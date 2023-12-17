using FluentCMS.Entities;
using FluentCMS.Repositories;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore(IUserRepository repository) : IProtectedUserStore<User>
{

}
