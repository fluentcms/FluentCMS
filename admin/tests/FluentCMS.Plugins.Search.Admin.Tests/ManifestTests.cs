using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCMS.Plugins.Search.Admin.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void TheManifestSaysWhatTheAttributesOfTheHalfDeclare() =>
        AdminPluginManifestCheck.VerifyFile(typeof(SearchAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
