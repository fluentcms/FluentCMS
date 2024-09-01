using FluentCMS.Services.Setup.Models;

namespace FluentCMS.Services.Setup;

public class SetupContext
{
    public SetupModel SetupRequest { get; set; }
    public User SuperAdmin { get; set; }
    public GlobalSettings GlobalSettings { get; set; }
    internal AdminTemplate AdminTemplate { get; set; }
    public IList<Layout> Layouts { get; set; } = [];
    public Guid DefaultLayoutId { get; set; }
    public IList<PluginDefinition> PluginDefinitions { get; set; } = [];
    public Site Site { get; set; }
    public IList<Page> Pages { get; set; } = [];

}
