namespace FluentCMS.Seeding;

/// <summary>
/// Puts starting content into an instance, such as the sample pack, a template or the fixtures of a test. A seeder writes through
/// the public services (<c>IContentService</c>, <c>ISettings&lt;T&gt;</c> and the rest), so permissions, versions, events, activity,
/// trash and scheduling behave as they do in real use.
/// </summary>
/// <remarks>
/// <para>
/// A seeder runs as the system principal with the <c>Seed</c> origin and the full service scope of the host, so it can do anything the
/// host can do. Trust comes from registering it: only seeders added with <c>cms.Seeding.Add</c> run.
/// </para>
/// <para>
/// The host records that a seeder has been applied, by <see cref="Name"/> and <see cref="Version"/>, and does not run it again at the
/// next start. That is what keeps content that was purged, renamed or changed from coming back. Raise <see cref="Version"/> to
/// run a seeder again. When it runs again, or after a start that stopped halfway, it must finish the job: look for each thing it
/// creates by a natural key that a person does not change (a slug), and bring what it finds to the state it wants (published,
/// translated, trashed, scheduled), instead of returning early because the entry exists.
/// </para>
/// </remarks>
public interface ISeeder
{
    /// <summary>The name under which the host records that the seeder was applied. It must not change between releases.</summary>
    string Name => GetType().FullName ?? GetType().Name;

    /// <summary>The version of the seed content. A seeder that was applied at a lower version runs again.</summary>
    int Version => 1;

    /// <summary>Writes the seed content.</summary>
    /// <param name="context">The operation to seed in, and the services to seed with.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>A task that completes when the content is written.</returns>
    Task SeedAsync(SeedContext context, CancellationToken cancellationToken = default);
}
