using System.Reflection;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.RichText.Tests;

// The admin knows the editor by name only: the first form with a rich text field loads its assembly (spec 2.6, 4.7).
public sealed class RichTextRegistrationTests
{
    [Fact]
    public async Task TheFirstLookupOfRichTextLoadsTheEditorsAssembly()
    {
        var loader = new RecordingLoader();
        await using var provider = new ServiceCollection().AddScoped<IAdminAssemblyLoader>(_ => loader).AddFieldEditors().BuildServiceProvider();
        var editors = provider.CreateScope().ServiceProvider.GetRequiredService<FieldEditorRegistry>();

        editors.Find("richText").ShouldBeNull();
        loader.Loaded.ShouldBeEmpty();

        (await editors.FindAsync("richText", TestContext.Current.CancellationToken))!.Editor.ShouldBe(typeof(RichTextFieldEditor));
        loader.Loaded.ShouldBe(["FluentCMS.Admin.RichText"]);
        editors.Find("richText")!.Editor.ShouldBe(typeof(RichTextFieldEditor));
    }

    [Fact]
    public void WithoutAnAssemblyLoaderRichTextHasNoEditor()
    {
        var editors = new ServiceCollection().AddFieldEditors().BuildServiceProvider().GetRequiredService<FieldEditorRegistry>();

        editors.Find("richText").ShouldBeNull();
    }

    private sealed class RecordingLoader : IAdminAssemblyLoader
    {
        public List<string> Loaded { get; } = [];

        public Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
        {
            Loaded.Add(assemblyName);
            return Task.FromResult(typeof(RichTextFieldEditor).Assembly);
        }
    }
}
