namespace FluentCMS.Seeding;

/// <summary>
/// Puts starting content into an instance, such as the sample pack, a template or the fixtures of a test. A seeder writes through
/// the public services (<c>IContentService</c>, <c>ISettings&lt;T&gt;</c> and the rest), so permissions, versions, events, activity,
/// trash and scheduling behave as they do in real use. It runs as the system principal with the <c>Seed</c> origin.
/// </summary>
/// <remarks>
/// A seeder must be safe to run again: it looks for what it would create, by a natural key such as a slug, and leaves it alone.
/// A seeder that creates nothing on its second run is idempotent; one that updates or removes what it finds is not seeding.
/// </remarks>
public interface ISeeder
{
    /// <summary>Writes the seed content.</summary>
    /// <param name="context">The operation to seed in, and the services to seed with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the content is written.</returns>
    Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default);
}
