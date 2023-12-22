using FluentCMS.Entities;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Identity;

public partial class UserStore : IUserSecurityStampStore<User>
{
    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.SecurityStamp);
    }

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }
}
