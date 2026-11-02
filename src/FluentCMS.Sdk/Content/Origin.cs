namespace FluentCMS.Content;

/// <summary>Where an operation entered the system. Persisted on every version and activity row.</summary>
public enum Origin
{
    /// <summary>The admin application, through its OAuth client.</summary>
    Admin = 1,

    /// <summary>The Delivery and Management APIs.</summary>
    Api = 2,

    /// <summary>The MCP plugin.</summary>
    Mcp = 3,

    /// <summary>The job runner.</summary>
    Job = 4,

    /// <summary>The command line tool.</summary>
    Cli = 5,

    /// <summary>Seeding of sample or initial content.</summary>
    Seed = 6,
}
