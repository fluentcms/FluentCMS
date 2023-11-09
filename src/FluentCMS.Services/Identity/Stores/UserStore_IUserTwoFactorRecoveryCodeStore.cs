using FluentCMS.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Services.Identity.Stores;

public partial class UserStore : IUserTwoFactorRecoveryCodeStore<User>
{
    public Task ReplaceCodesAsync(User user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken)
    {
        var rcs = recoveryCodes.Select(x => new TwoFactorRecoveryCode { Code = x, Redeemed = false }).ToList();
        user.RecoveryCodes.Clear();
        user.RecoveryCodes.AddRange(rcs);
        return Task.CompletedTask;
    }

    public Task<bool> RedeemCodeAsync(User user, string code, CancellationToken cancellationToken)
    {
        var rc = user.RecoveryCodes.FirstOrDefault(x => x.Code == code);
        if (rc != null) rc.Redeemed = true;
        return Task.FromResult(true);
    }

    public Task<int> CountCodesAsync(User user, CancellationToken cancellationToken)
    {
        return Task.FromResult(user.RecoveryCodes.Count);
    }
}
