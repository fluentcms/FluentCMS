using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

// A fresh install asks for its first administrator before anything else is reachable (F-USR acceptance).
public interface IFirstRunSetup
{
    Task<bool> NeedsFirstAdministratorAsync(CancellationToken cancellationToken = default);

    Task<Result<Guid>> CreateFirstAdministratorAsync(NewUser user, CancellationToken cancellationToken = default);
}
