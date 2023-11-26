using FluentCMS.Entities;

namespace FluentCMS.Api;

public class DefaultData
{
    public required Host Host { get; set; }
    public required DefaultUser SuperAdmin { get; set; }
    public required Site Site { get; set; }
    public required List<Page> Pages { get; set; }
    public required List<PluginDefinition> PluginDefinitions { get; set; }

    public class DefaultUser
    {
        public required string UserName { get; set; }
        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}
