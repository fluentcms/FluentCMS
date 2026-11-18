using FluentCMS.Admin.Plugins;
using FluentCMS.Plugins.Media.Admin;

[assembly: FluentCmsAdminPlugin(typeof(MediaAdminPlugin))]

namespace FluentCMS.Plugins.Media.Admin;

// The media library (A-21), its field editor, list cell and picker (A-20) and its widget (A-22) join this half.
[FluentCmsAdminPlugin("media", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class MediaAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin) => ArgumentNullException.ThrowIfNull(admin);
}
