namespace FluentCMS.Seeding;

/// <summary>Runs the registered seeders. The host does it once on start when there are any; call it to run them again.</summary>
public interface ISeedRunner
{
    /// <summary>
    /// Runs every registered seeder in registration order, on the default site and in its default locale, each as the system
    /// principal with the <c>Seed</c> origin. A seeder that throws stops the run.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when every seeder has run.</returns>
    Task RunAsync(CancellationToken cancellationToken = default);
}
