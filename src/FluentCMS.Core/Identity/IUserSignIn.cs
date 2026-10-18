using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

// Sign-in for people; agents authenticate with their own credentials and never through this (ADR-0009).
public interface IUserSignIn
{
    // Every failure carries the same error, so a caller cannot tell an unknown email from a wrong password or a disabled account.
    Task<Result> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
