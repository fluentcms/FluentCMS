namespace FluentCMS.Core.Sites;

// One site per instance until multi-site (MST-01, M3); every site-scoped record carries its id from the start (SCH-07).
public sealed record Site(Guid Id, string Name)
{
    public static Site Default { get; } = new(new Guid("00000000-0000-0000-0000-000000000001"), "Default");
}
