using FluentCMS.Core.Audit;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Infrastructure.Identity;

internal sealed class UserDirectory(
    UserManager<FluentCmsUser> users,
    RoleManager<FluentCmsRole> roles,
    IUnitOfWork unitOfWork,
    IAuditLog auditLog,
    ICurrentInitiator currentInitiator,
    TimeProvider clock) : IUserDirectory
{
    private const string SubjectType = "user";

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await users.Users.OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var summaries = new List<UserSummary>(stored.Count);

        foreach (var user in stored)
        {
            summaries.Add(new UserSummary(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                Accounts.IsDisabled(user),
                (await users.GetRolesAsync(user)).ToList()));
        }

        return summaries;
    }

    public async Task<Result<Guid>> CreateAsync(NewUser newUser, CancellationToken cancellationToken = default)
    {
        if (RefuseNonPerson("create") is { } refused)
        {
            return Result<Guid>.Failure(refused.Errors);
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var created = await Accounts.CreateAdministratorAsync(users, roles, clock, newUser);
        if (!created.IsSuccess)
        {
            return created;
        }

        await auditLog.AppendAsync(new NewAuditEvent("user.created") { SubjectType = SubjectType, SubjectId = created.Value }, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return created;
    }

    public async Task<Result> DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (RefuseNonPerson("disable") is { } refused)
        {
            return refused;
        }

        if (currentInitiator.Current is UserInitiator self && self.UserId == userId)
        {
            return Result.Failure(UserErrorCodes.CannotDisableSelf, "You cannot disable your own account.");
        }

        return await ChangeAsync(userId, "user.disabled", async user =>
        {
            var result = await users.SetLockoutEnabledAsync(user, true);
            if (result.Succeeded)
            {
                result = await users.SetLockoutEndDateAsync(user, Accounts.NoEnd);
            }

            // A new security stamp invalidates the cookies already issued once they are next validated.
            if (result.Succeeded)
            {
                result = await users.UpdateSecurityStampAsync(user);
            }

            return result;
        }, cancellationToken);
    }

    public async Task<Result> EnableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (RefuseNonPerson("enable") is { } refused)
        {
            return refused;
        }

        return await ChangeAsync(userId, "user.enabled", async user =>
        {
            var result = await users.SetLockoutEndDateAsync(user, null);
            if (result.Succeeded)
            {
                result = await users.ResetAccessFailedCountAsync(user);
            }

            return result;
        }, cancellationToken);
    }

    private async Task<Result> ChangeAsync(
        Guid userId,
        string operation,
        Func<FluentCmsUser, Task<IdentityResult>> change,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure(UserErrorCodes.UserNotFound, $"No user has the id {userId}.");
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);

        var changed = await change(user);
        if (!changed.Succeeded)
        {
            return Result.Failure(Accounts.ToErrors(changed));
        }

        await auditLog.AppendAsync(new NewAuditEvent(operation) { SubjectType = SubjectType, SubjectId = user.Id }, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success();
    }

    // Who may sign in is a person's decision: no agent, whatever its scope (SEC-02), and no engine (ADR-0012).
    private Result? RefuseNonPerson(string action)
    {
        return currentInitiator.Current is AgentInitiator or SystemInitiator
            ? Result.Failure(UserErrorCodes.PeopleOnly, $"Only a person can {action} a user; {currentInitiator.Current.DisplayName} cannot.")
            : null;
    }
}
