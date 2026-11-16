namespace FluentCMS.Admin.Configuration;

// The outcome of reading config.json at start: the configuration when it is valid, otherwise the problems the error page names.
internal sealed class AdminStartup
{
    public AdminStartup(AdminConfiguration configuration, IReadOnlyList<string> unknownMembers)
    {
        Configuration = configuration;
        Problems = [];
        UnknownMembers = unknownMembers;
    }

    public AdminStartup(IReadOnlyList<ConfigurationProblem> problems, IReadOnlyList<string> unknownMembers)
    {
        Problems = problems;
        UnknownMembers = unknownMembers;
    }

    public AdminConfiguration? Configuration { get; }

    public IReadOnlyList<ConfigurationProblem> Problems { get; }

    public IReadOnlyList<string> UnknownMembers { get; }
}
