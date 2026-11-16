namespace FluentCMS.Admin.Api;

/// <summary>The access token of the signed-in user, which the API client sends as <c>Authorization: Bearer</c>. The session fills it.</summary>
public interface IAccessTokenSource
{
    /// <summary>The current access token, refreshed if it is about to expire.</summary>
    /// <param name="cancellationToken">Cancels the wait for a refresh.</param>
    /// <returns>The token, or <see langword="null"/> when no one is signed in, in which case no <c>Authorization</c> is sent.</returns>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}
