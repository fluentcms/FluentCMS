using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

// Each change runs in a unit of work and appends its audit event, naming the signed-in administrator (AUD-N1).
public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default);

    // Every user created in M1 is an administrator (D23).
    Task<Result<Guid>> CreateAsync(NewUser user, CancellationToken cancellationToken = default);

    // Ends the user's open sessions too. Nobody can disable themselves, which in M1 also keeps the last active administrator.
    Task<Result> DisableAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Result> EnableAsync(Guid userId, CancellationToken cancellationToken = default);
}
