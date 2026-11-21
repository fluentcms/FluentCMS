using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void TheManifestSaysWhatTheAttributesOfTheHalfDeclare() =>
        AdminPluginManifestCheck.VerifyFile(typeof(AiAssistAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
