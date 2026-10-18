using FluentCMS.Core.Results;

namespace FluentCMS.Core.Identity;

// Sign-in for people; agents authenticate with their own credentials and never through this (ADR-0009).
public interface IUserSignIn
{
    // Every failure carries the same error, so a caller cannot tell an unknown email from a wrong password or a disabled account.
    Task<Result> SignInAsync(string email, string password, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    // The external identity providers the owner configured; empty when there are none (P10).
    Task<IReadOnlyList<ExternalProvider>> GetExternalProvidersAsync(CancellationToken cancellationToken = default);

    // Completes the sign-in an external provider has just vouched for. There is no self-registration in M1:
    // an identity that matches no user, by an earlier link or by a verified email, fails and nothing is linked.
    Task<Result> SignInExternalAsync(CancellationToken cancellationToken = default);
}

public sealed record ExternalProvider(string Scheme, string DisplayName);
