using FluentCMS.Admin.Plugins;
using Xunit;

namespace FluentCMS.Plugins.Webhooks.Admin.Tests;

public sealed class ManifestTests
{
    [Fact]
    public void TheManifestSaysWhatTheAttributesOfTheHalfDeclare() =>
        AdminPluginManifestCheck.VerifyFile(typeof(WebhooksAdminPlugin).Assembly, Path.Combine(AppContext.BaseDirectory, AdminPluginManifest.FileName));
}
