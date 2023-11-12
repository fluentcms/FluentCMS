using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore: IProtectedUserStore<User>
{
    private readonly IUserRepository _repository;

    public UserStore(IUserRepository repository)
    {
        _repository = repository;
    }
}