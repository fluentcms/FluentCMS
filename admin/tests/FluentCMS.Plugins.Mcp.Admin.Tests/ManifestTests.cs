using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void TheManifestSaysWhatTheAttributesOfTheHalfDeclare() =>
        AdminPluginManifestCheck.VerifyFile(typeof(McpAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
