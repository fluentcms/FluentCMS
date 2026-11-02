namespace FluentCMS.Content;

/// <summary>The kind of identity behind a <see cref="Principal"/>.</summary>
public enum PrincipalKind
{
    /// <summary>A user of the admin application.</summary>
    AdminUser = 1,

    /// <summary>A member of the member pool.</summary>
    Member = 2,

    /// <summary>An API key.</summary>
    ApiKey = 3,

    /// <summary>A non-human identity that authenticates with client credentials.</summary>
    ServiceAccount = 4,

    /// <summary>The built-in identity for jobs, seeding and the command line tool.</summary>
    System = 5,

    /// <summary>A visitor with no identity, who gets what the Public role of the site grants.</summary>
    Anonymous = 6,
}
