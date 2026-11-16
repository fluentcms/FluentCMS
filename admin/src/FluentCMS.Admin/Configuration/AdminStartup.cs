namespace FluentCMS.Admin.Configuration;

// The outcome of reading config.json at start: the configuration when it is valid, otherwise the problems the error page names.
internal sealed class AdminStartup
{
    public AdminStartup(AdminConfiguration configuration, IReadOnlyList<string> unknownMembers, IReadOnlyList<string> plainHttpMembers)
    {
        Configuration = configuration;
        Problems = [];
        UnknownMembers = unknownMembers;
        PlainHttpMembers = plainHttpMembers;
    }

    public AdminStartup(IReadOnlyList<ConfigurationProblem> problems, IReadOnlyList<string> unknownMembers)
    {
        Problems = problems;
        UnknownMembers = unknownMembers;
        PlainHttpMembers = [];
    }

    public AdminConfiguration? Configuration { get; }

    public IReadOnlyList<ConfigurationProblem> Problems { get; }

    public IReadOnlyList<string> UnknownMembers { get; }

    // apiBaseUrl or authority when it is plain http on a host that is not loopback, which is worth a warning, not a refusal.
    public IReadOnlyList<string> PlainHttpMembers { get; }
}
