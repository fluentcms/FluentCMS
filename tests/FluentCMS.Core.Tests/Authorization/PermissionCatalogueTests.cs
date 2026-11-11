using FluentCMS.Authorization;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Tests.Schema;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Authorization;

public class PermissionCatalogueTests
{
    private static readonly string[] SpecCore =
    [
        "users.manage", "roles.manage", "apikeys.manage", "sites.manage", "jobs.manage",
        "activity.read", "schema.read", "plugins.manage", "transfer.manage", "system.read", "members.manage",
    ];

    private static readonly string[] Operations = ["create", "read", "update", "delete", "publish", "unpublish", "restore", "purge"];

    private static PermissionCatalogue Build(params IPermissionProvider[] providers) =>
        new(SampleSchema.FromClasses().Build(), providers);

    [Fact]
    public void ListsEveryCorePermissionOfTheSpecification()
    {
        var catalogue = Build();

        foreach (var name in SpecCore)
        {
            catalogue.Find(name)!.Source.ShouldBe(PermissionSource.Core);
        }
    }

    [Fact]
    public void ListsEveryOperationForEveryContentType()
    {
        var schema = SampleSchema.FromClasses().Build();
        var catalogue = new PermissionCatalogue(schema, []);

        schema.ContentTypes.ShouldNotBeEmpty();
        foreach (var type in schema.ContentTypes)
        {
            foreach (var operation in Operations)
            {
                catalogue.Find($"content.{type.Name}.{operation}")!.Source.ShouldBe(PermissionSource.Content);
            }
        }

        catalogue.Entries.Count(entry => entry.Source == PermissionSource.Content).ShouldBe(schema.ContentTypes.Count * Operations.Length);
    }

    [Fact]
    public void BlocksAndComponentsHaveNoPermissions()
    {
        var schema = SampleSchema.FromClasses().Build();
        var catalogue = new PermissionCatalogue(schema, []);

        (schema.Blocks.Count + schema.Components.Count).ShouldBeGreaterThan(0);
        foreach (var type in schema.Blocks.Concat(schema.Components))
        {
            catalogue.Contains($"content.{type.Name}.read").ShouldBeFalse();
        }
    }

    [Fact]
    public void ListsThePermissionsOfAPlugin()
    {
        var catalogue = Build(new TestPlugin());

        catalogue.Find("media.manage")!.Source.ShouldBe(PermissionSource.Plugin);
        catalogue.Find("settings.media.manage")!.Description.ShouldBe("Change the media settings.");
        catalogue.Entries.Select(entry => entry.Name).ShouldBeUnique();
    }

    [Fact]
    public void RefusesAPermissionTwice()
    {
        Should.Throw<FluentCmsConfigurationException>(() => Build(new TestPlugin(), new TestPlugin())).Code.ShouldBe("FCMS1401");
        Should.Throw<FluentCmsConfigurationException>(() => Build(new Declares("users.manage"))).Code.ShouldBe("FCMS1401");
    }

    [Fact]
    public void RefusesAPluginInTheContentNamespace()
    {
        Should.Throw<FluentCmsConfigurationException>(() => Build(new Declares("content.article.read"))).Code.ShouldBe("FCMS1402");
        Should.Throw<FluentCmsConfigurationException>(() => Build(new Declares("content.invented.read"))).Code.ShouldBe("FCMS1402");
    }

    [Fact]
    public void AGrantMustNameSomethingInTheCatalogue()
    {
        var catalogue = Build(new TestPlugin());

        catalogue.IsGrantable("*").ShouldBeTrue();
        catalogue.IsGrantable("content.*.read").ShouldBeTrue();
        catalogue.IsGrantable("content.article.purge").ShouldBeTrue();
        catalogue.IsGrantable("settings.*.manage").ShouldBeTrue();
        catalogue.IsGrantable("media.manage").ShouldBeTrue();

        catalogue.IsGrantable("").ShouldBeFalse();
        catalogue.IsGrantable("content.invented.read").ShouldBeFalse();
        catalogue.IsGrantable("content.*").ShouldBeFalse();
        catalogue.IsGrantable("Content.*.read").ShouldBeFalse();
        catalogue.IsGrantable("users.*.manage").ShouldBeFalse();
    }

    [Theory]
    [InlineData("*", "anything.at.all", true)]
    [InlineData("content.*.read", "content.article.read", true)]
    [InlineData("content.*.read", "content.article.update", false)]
    [InlineData("content.*.read", "content.article.sub.read", false)]
    [InlineData("content.article.read", "content.article.read", true)]
    [InlineData("content.article.read", "content.author.read", false)]
    [InlineData("media.manage", "media.manage.more", false)]
    public void PatternsMatchWholeSegments(string grant, string permission, bool expected) =>
        PermissionPattern.Matches(grant, permission).ShouldBe(expected);

    private sealed class TestPlugin : IPermissionProvider
    {
        public IEnumerable<Permission> GetPermissions() =>
        [
            new("media.manage", "Manage the media library."),
            new("settings.media.manage", "Change the media settings."),
        ];
    }

    private sealed class Declares(string name) : IPermissionProvider
    {
        public IEnumerable<Permission> GetPermissions() => [new(name, "Declared for a test.")];
    }
}
