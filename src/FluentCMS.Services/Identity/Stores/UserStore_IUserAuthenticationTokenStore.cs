using FluentCMS.Entities;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore : IUserAuthenticationTokenStore<User>
{
    public Task SetTokenAsync(User user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        var token = user.Tokens.FirstOrDefault(x => x.LoginProvider == loginProvider && x.Name == name);

        if (token == null)
        {
            token = new IdentityUserToken<Guid>
            {
                LoginProvider = loginProvider,
                Name = name,
                Value = value,
                UserId = user.Id
            };
            user.Tokens.Add(token);
        }
        else
        {
            token.Value = value;
        }
        return Task.CompletedTask;
    }

    public Task RemoveTokenAsync(User user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var userTokens = user.Tokens ?? new List<IdentityUserToken<Guid>>();
        userTokens.RemoveAll(x => x.LoginProvider == loginProvider && x.Name == name);

        return Task.CompletedTask;
    }

    public Task<string?> GetTokenAsync(User user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var userTokens = user.Tokens ?? [];
        var token = userTokens.FirstOrDefault(x => x.LoginProvider == loginProvider && x.Name == name);
        return Task.FromResult(token?.Value);
    }
}
