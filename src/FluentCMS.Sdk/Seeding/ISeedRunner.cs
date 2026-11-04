namespace FluentCMS.Seeding;

/// <summary>Runs the seeders that were added with <c>cms.Seeding.Add</c>. The host does it on start unless told not to; call it to run them at another time.</summary>
public interface ISeedRunner
{
    /// <summary>
    /// Runs each registered seeder that has not been applied at its current version, in registration order, on the default site and in its
    /// default locale, as the system principal with the <c>Seed</c> origin. An instance that finds another instance running a seeder skips it
    /// and returns; a run that has not finished after the claim timeout (15 minutes unless changed) can be taken over. A seeder that
    /// throws is not marked as applied and stops the run.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when every seeder has run or been skipped.</returns>
    Task RunAsync(CancellationToken cancellationToken = default);
}
