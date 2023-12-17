using FluentCMS.Entities;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore : IUserClaimStore<User>
{
    public Task AddClaimsAsync(User user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        foreach (var claim in claims)
        {
            var identityClaim = new IdentityUserClaim<Guid>()
            {
                ClaimType = claim.Type,
                ClaimValue = claim.Value,
                UserId = user.Id
            };

            user.Claims.Add(identityClaim);
        }
        return Task.CompletedTask;
    }

    public Task ReplaceClaimAsync(User user, Claim claim, Claim newClaim, CancellationToken cancellationToken)
    {
        user.Claims.RemoveAll(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);

        var identityClaim = new IdentityUserClaim<Guid>()
        {
            ClaimType = newClaim.Type,
            ClaimValue = newClaim.Value,
            UserId = user.Id
        };

        user.Claims.Add(identityClaim);

        return Task.CompletedTask;
    }

    public Task RemoveClaimsAsync(User user, IEnumerable<Claim> claims, CancellationToken cancellationToken)
    {
        foreach (var claim in claims)
            user.Claims.RemoveAll(x => x.ClaimType == claim.Type && x.ClaimValue == claim.Value);

        return Task.CompletedTask;
    }

    public async Task<IList<User>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken)
    {
        return await repository.GetUsersForClaim(claim, cancellationToken);
    }
}
