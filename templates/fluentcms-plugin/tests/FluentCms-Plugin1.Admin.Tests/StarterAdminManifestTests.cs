using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCms_Plugin1.Admin.Tests;

public sealed class StarterAdminManifestTests
{
    // The admin reads wwwroot/fluentcms-admin.json before it loads the half, so a contribution the file leaves out is never
    // shown, and one it names that the assembly does not declare fails when it is used. Change the file with the attributes.
    [Fact]
    public void TheManifestMatchesTheAttributesOfTheHalf() =>
        AdminPluginManifestCheck.VerifyFile(typeof(StarterAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
