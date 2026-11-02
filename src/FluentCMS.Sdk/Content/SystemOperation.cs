using FluentCMS.Sites;

namespace FluentCMS.Content;

/// <summary>
/// Runs code as <see cref="Principal.System"/>, for jobs, seeding and the command line tool. A request never gets here:
/// only an origin that is not a client may use it, and the content service refuses the system principal from any other.
/// </summary>
public static class SystemOperation
{
    /// <summary>Whether an origin may run as the system principal.</summary>
    /// <param name="origin">The origin.</param>
    /// <returns><see langword="true"/> for <see cref="Origin.Job"/>, <see cref="Origin.Cli"/> and <see cref="Origin.Seed"/>.</returns>
    public static bool Allows(Origin origin) => origin is Origin.Job or Origin.Cli or Origin.Seed;

    /// <summary>Sets the ambient operation to the system principal until the result is disposed, which puts back the operation before it.</summary>
    /// <param name="accessor">The ambient operation.</param>
    /// <param name="origin">Where the work comes from: <see cref="Origin.Job"/>, <see cref="Origin.Cli"/> or <see cref="Origin.Seed"/>.</param>
    /// <param name="site">The site to work in.</param>
    /// <param name="locale">The locale to work in, or empty for the default locale of the site.</param>
    /// <param name="correlationId">An id that ties the work together, or <see langword="null"/> for a new one.</param>
    /// <returns>What ends the operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="origin"/> is that of a client.</exception>
    public static IDisposable Begin(IOperationContextAccessor accessor, Origin origin, SiteId site, string locale = "", string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(locale);

        if (!Allows(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Only a job, the command line tool or seeding runs as the system principal.");
        }

        var scope = new Scope(accessor, accessor.Current);
        accessor.Current = new OperationContext(
            Principal.System,
            origin,
            site,
            locale,
            null,
            correlationId ?? Guid.CreateVersion7().ToString(),
            DateTimeOffset.UtcNow);
        return scope;
    }

    private sealed class Scope(IOperationContextAccessor accessor, OperationContext? previous) : IDisposable
    {
        public void Dispose() => accessor.Current = previous;
    }
}
