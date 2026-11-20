using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCMS.Plugins.MultiSite.Admin.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void TheManifestSaysWhatTheAttributesOfTheHalfDeclare() =>
        AdminPluginManifestCheck.VerifyFile(typeof(MultiSiteAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
