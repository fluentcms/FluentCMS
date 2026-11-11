using FluentCMS.Content;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Sites;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Identity.Members;

internal sealed record MemberGroupRef(Guid Id, string Name);

// A member as the APIs show it: to an administrator and, on /me, to the member itself.
internal sealed record MemberInfo(
    Guid Id,
    string Email,
    bool EmailVerified,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? DeactivatedAt,
    DateTimeOffset? LockoutEnd,
    IReadOnlyList<MemberGroupRef> Groups);

// A refusal of a call that is for a member, by a caller that is not one: 403, or 401 when it is not signed in.
internal sealed class MemberAccessDeniedException(string message) : Exception(message);

// Registration is off, so the route that registers is not there: 404.
internal sealed class MemberRegistrationClosedException() : Exception("Registration is not open on this instance.");

internal static class MemberViews
{
    public static async Task<ILookup<Guid, MemberGroupRef>> GroupsOfAsync(SystemDbContext context, List<Guid> memberIds, CancellationToken cancellationToken)
    {
        var rows = await context.Set<MemberGroupMember>().AsNoTracking()
            .Where(membership => memberIds.Contains(membership.MemberId))
            .Join(context.Set<MemberGroup>(), membership => membership.GroupId, group => group.Id, (membership, group) => new { membership.MemberId, group.Id, group.Name })
            .ToListAsync(cancellationToken);

        return rows.OrderBy(row => row.Name, StringComparer.Ordinal).ToLookup(row => row.MemberId, row => new MemberGroupRef(row.Id, row.Name));
    }

    public static MemberInfo Info(CmsMember member, IEnumerable<MemberGroupRef> groups) =>
        new(member.Id, member.Email ?? "", member.EmailConfirmed, member.DisplayName, member.CreatedAt, member.LastLoginAt, member.DeactivatedAt, member.LockoutEnd, [.. groups]);

    public static string? DisplayName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    // Identity's refusals as problems with the codes of the registry.
    public static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ValidationException([.. result.Errors.Select(error => new ValidationError(PathOf(error.Code), CodeOf(error.Code), error.Description))]);
        }
    }

    public static ValidationException InvalidToken() =>
        new([new ValidationError("token", MemberErrorCodes.InvalidToken, "The link is not valid: it was used already, it has expired, or it is not of this address.")]);

    public static ValidationException NotFound(Guid id) =>
        new([new ValidationError("id", MemberErrorCodes.MemberNotFound, $"There is no member {id:D}.")]);

    private static string CodeOf(string code) => code switch
    {
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName) => MemberErrorCodes.EmailTaken,
        nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => MemberErrorCodes.InvalidEmail,
        nameof(IdentityErrorDescriber.InvalidToken) => MemberErrorCodes.InvalidToken,
        nameof(IdentityErrorDescriber.PasswordMismatch) => MemberErrorCodes.WrongPassword,
        nameof(IdentityErrorDescriber.ConcurrencyFailure) => MemberErrorCodes.MemberChanged,
        _ when code.StartsWith("Password", StringComparison.Ordinal) => MemberErrorCodes.InvalidPassword,
        _ when code.StartsWith("FCMS", StringComparison.Ordinal) => code,
        _ => MemberErrorCodes.InvalidMember,
    };

    private static string PathOf(string code) => code switch
    {
        nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)
            or nameof(IdentityErrorDescriber.InvalidEmail) or nameof(IdentityErrorDescriber.InvalidUserName) => "email",
        nameof(IdentityErrorDescriber.InvalidToken) => "token",
        nameof(IdentityErrorDescriber.PasswordMismatch) => "currentPassword",
        MemberValidator.InvalidDisplayName => "displayName",
        _ when code.StartsWith("Password", StringComparison.Ordinal) => "password",
        _ => "",
    };
}

// A member acting on its own account, on a route that anyone may call or on a hosted page: the operation runs as the
// member until the result is disposed, so that the events of the change name the member as their actor.
internal static class MemberOperation
{
    public static IDisposable Begin(IOperationContextAccessor accessor, CmsMember member, TimeProvider time)
    {
        var previous = accessor.Current;
        var principal = new Principal(PrincipalKind.Member, member.Id, member.Email ?? "");
        accessor.Current = previous is not null
            ? previous with { Principal = principal, Actor = null }
            : new OperationContext(principal, Origin.Api, new SiteId(DefaultSite.Id), DefaultSite.Locale, null, System.Diagnostics.Activity.Current?.Id ?? Guid.CreateVersion7().ToString(), time.GetUtcNow());
        return new Scope(accessor, previous);
    }

    private sealed class Scope(IOperationContextAccessor accessor, OperationContext? previous) : IDisposable
    {
        public void Dispose() => accessor.Current = previous;
    }
}
