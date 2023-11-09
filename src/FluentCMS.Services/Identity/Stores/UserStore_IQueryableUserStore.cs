using FluentCMS.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore : IQueryableUserStore<User>
{
    public IQueryable<User> Users => _repository.AsQueryable();
}