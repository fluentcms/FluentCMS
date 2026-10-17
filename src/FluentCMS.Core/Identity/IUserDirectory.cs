using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default);

    // Every user created in M1 is an administrator (D23).
    Task<Result<Guid>> CreateAsync(NewUser user, CancellationToken cancellationToken = default);

    Task<Result> DisableAsync(Guid userId, CancellationToken cancellationToken = default);
}
