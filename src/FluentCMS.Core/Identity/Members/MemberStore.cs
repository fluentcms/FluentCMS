using System.Security.Claims;
using FluentCMS.Core.Storage;
using FluentCMS.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.Members;

// Identity's EF store over cms_member, cms_member_login and cms_member_token. A member's name is its address, so a
// lookup by name is a lookup by address, and a member has no claims of its own. A change is described from what was
// stored before it and raised as the member events of the catalogue, in the transaction of the change. A member can be
// deleted, unlike an admin user: its logins, tokens and group memberships go with it, and activity rows keep its id.
internal sealed class MemberStore(SystemDbContext context, IdentityChanges changes, TimeProvider time, IdentityErrorDescriber? describer = null)
    : UserOnlyStore<CmsMember, SystemDbContext, Guid, CmsMemberClaim, CmsMemberLogin, CmsMemberToken>(context, describer)
{
    // Set by MemberManager while a password is checked: a check that rehashes the password saves it again, which is not
    // a new password.
    internal bool Rehashing { get; set; }

    public override Task<IdentityResult> CreateAsync(CmsMember user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.CreatedAt == default)
        {
            user.CreatedAt = time.GetUtcNow();
        }

        return changes.RunAsync(
            async () =>
            {
                var result = await base.CreateAsync(user, cancellationToken);
                if (result.Succeeded)
                {
                    await changes.RaiseAsync(new MemberRegisteredEvent(user.Id, user.Email ?? ""), cancellationToken);
                    if (user.EmailConfirmed)
                    {
                        await changes.RaiseAsync(new MemberVerifiedEvent(user.Id, user.Email ?? ""), cancellationToken);
                    }
                }

                return result;
            },
            cancellationToken);
    }

    public override async Task<IdentityResult> UpdateAsync(CmsMember user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var before = await StoredAsync(user, cancellationToken);
        return await changes.RunAsync(
            async () =>
            {
                var result = await base.UpdateAsync(user, cancellationToken);
                if (!result.Succeeded || before is null)
                {
                    return result;
                }

                var email = user.Email ?? "";
                if (!before.EmailConfirmed && user.EmailConfirmed)
                {
                    await changes.RaiseAsync(new MemberVerifiedEvent(user.Id, email), cancellationToken);
                }

                if (before.DeactivatedAt is null && user.DeactivatedAt is not null)
                {
                    await changes.RaiseAsync(new MemberDeactivatedEvent(user.Id, email), cancellationToken);
                }

                if (Changes(before, user) is { Count: > 0 } changed)
                {
                    await changes.RaiseAsync(new MemberUpdatedEvent(user.Id, email, changed), cancellationToken);
                }

                return result;
            },
            cancellationToken);
    }

    public override Task<IdentityResult> DeleteAsync(CmsMember user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        return changes.RunAsync(
            async () =>
            {
                var result = await base.DeleteAsync(user, cancellationToken);
                if (result.Succeeded)
                {
                    await changes.RaiseAsync(new MemberDeletedEvent(user.Id, user.Email ?? ""), cancellationToken);
                }

                return result;
            },
            cancellationToken);
    }

    public override Task<CmsMember?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default) =>
        FindByEmailAsync(normalizedUserName, cancellationToken);

    public override Task<IList<Claim>> GetClaimsAsync(CmsMember user, CancellationToken cancellationToken = default) =>
        Task.FromResult<IList<Claim>>([]);

    public override Task AddClaimsAsync(CmsMember user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default) =>
        throw NoClaims();

    public override Task ReplaceClaimAsync(CmsMember user, Claim claim, Claim newClaim, CancellationToken cancellationToken = default) =>
        throw NoClaims();

    public override Task RemoveClaimsAsync(CmsMember user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default) =>
        throw NoClaims();

    public override Task<IList<CmsMember>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default) =>
        Task.FromResult<IList<CmsMember>>([]);

    // One statement, outside Identity's update: a sign-in is not a change of the member, and it must not fail on the
    // concurrency stamp of a change made at the same time.
    public Task TouchLastLoginAsync(Guid memberId, CancellationToken cancellationToken) =>
        Context.Set<CmsMember>()
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(member => member.LastLoginAt, time.GetUtcNow()), cancellationToken);

    private static NotSupportedException NoClaims() => new("A member has no claims of its own; its groups are rows of cms_member_group_member.");

    // The member as it is stored. A tracked member keeps what was read; one the caller built is read again.
    private async Task<CmsMember?> StoredAsync(CmsMember user, CancellationToken cancellationToken)
    {
        var entry = Context.Entry(user);
        if (entry.State != EntityState.Detached)
        {
            return (CmsMember)entry.OriginalValues.ToObject();
        }

        return await Context.Set<CmsMember>().AsNoTracking().FirstOrDefaultAsync(stored => stored.Id == user.Id, cancellationToken);
    }

    // What an administrator would call a change. The stamps, the last sign-in and the counters of failed sign-ins are not.
    private List<string> Changes(CmsMember before, CmsMember after)
    {
        List<string> changed = [];
        Add(MemberChanges.DisplayName, before.DisplayName != after.DisplayName);
        Add(MemberChanges.Password, !Rehashing && before.PasswordHash != after.PasswordHash);
        Add(MemberChanges.Reactivated, before.DeactivatedAt is not null && after.DeactivatedAt is null);
        return changed;

        void Add(string name, bool differs)
        {
            if (differs)
            {
                changed.Add(name);
            }
        }
    }
}
