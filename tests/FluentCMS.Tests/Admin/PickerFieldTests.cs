using System.Text.Json;
using Bunit;
using FluentCMS.Admin.Components.Forms;
using FluentCMS.Admin.Forms;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using FluentCMS.Core.Search;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class PickerFieldTests
{
    internal static readonly Guid Drill = Guid.Parse("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e");
    internal static readonly Guid Hero = Guid.Parse("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6f");

    [Fact]
    public void Before_the_circuit_attaches_a_picker_is_the_plain_select()
    {
        using var context = Context(interactive: false);

        var cut = context.Render<PickerField>(EntryPicker());

        Assert.Equal("Form.Values[product]", cut.Find("select").GetAttribute("name"));
        Assert.True(cut.Find($"option[value='{Drill}']").HasAttribute("selected"));
        Assert.Empty(cut.FindAll("input[type=search]"));
    }

    [Fact]
    public void An_entry_picker_searches_the_target_type_and_posts_the_chosen_id()
    {
        using var context = Context(interactive: true);
        var search = (FakeSearch)context.Services.GetRequiredService<ISearch>();

        var cut = context.Render<PickerField>(EntryPicker(chosen: false));
        Assert.Equal("", cut.Find("input[type=hidden]").GetAttribute("value") ?? "");
        cut.Find("input[type=search]").Input("dri");

        Assert.Equal(("dri", "product"), (search.Queries[0].Text, search.Queries[0].ContentTypes!.Single()));
        var option = cut.Find("[role=option]");
        Assert.Contains("Cordless drill", option.TextContent);
        Assert.Equal("rove", cut.Find("[role=listbox]").GetAttribute("data-ui"));
        Assert.Equal(cut.Find("[role=listbox]").Id, cut.Find("input[type=search]").GetAttribute("aria-controls"));

        option.Click();

        Assert.Equal(Drill.ToString(), cut.Find("input[type=hidden]").GetAttribute("value"));
        Assert.Contains("Cordless drill", cut.Find("[data-testid=picker-choice]").TextContent);
        Assert.Empty(cut.FindAll("[role=option]"));

        cut.Find("[data-action=clear]").Click();
        Assert.Null(cut.Find("input[type=hidden]").GetAttribute("value"));
    }

    [Fact]
    public void Escape_clears_the_matches_and_a_search_without_matches_says_so()
    {
        using var context = Context(interactive: true);

        var cut = context.Render<PickerField>(EntryPicker());
        cut.Find("input[type=search]").Input("saw");
        Assert.Contains("Nothing matches.", cut.Markup);

        cut.Find("input[type=search]").Input("dri");
        cut.Find("input[type=search]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=option]"));
    }

    [Fact]
    public void A_media_picker_finds_assets_by_file_name_with_a_preview()
    {
        using var context = Context(interactive: true);

        var cut = context.Render<PickerField>(parameters => parameters
            .Add(p => p.Field, "hero")
            .Add(p => p.Kind, PickerField.MediaKind)
            .Add(p => p.Label, "hero"));
        cut.Find("input[type=search]").Input("HERO");

        var option = cut.Find("[role=option]");
        Assert.Contains("hero.jpg (products)", option.TextContent);
        Assert.Equal($"media/{Hero}/file", option.QuerySelector("img")!.GetAttribute("src"));
        option.Click();
        Assert.Equal(Hero.ToString(), cut.Find("input[type=hidden]").GetAttribute("value"));
    }

    internal static AdminTestContext Context(bool interactive)
    {
        var context = new AdminTestContext();
        AddServices(context);
        context.Renderer.SetRendererInfo(new RendererInfo(interactive ? "Server" : "Static", interactive));
        return context;
    }

    internal static void AddServices(AdminTestContext context)
    {
        context.Services.AddSingleton<ISearch>(new FakeSearch());
        context.Services.AddSingleton<IEntryDirectory>(new FakeEntries());
        context.Services.AddSingleton<IMediaLibrary>(new FakeMedia());
    }

    private static Action<ComponentParameterCollectionBuilder<PickerField>> EntryPicker(bool chosen = true)
    {
        return parameters => parameters
            .Add(p => p.Field, "product")
            .Add(p => p.Kind, PickerField.EntryKind)
            .Add(p => p.Target, "product")
            .Add(p => p.Value, chosen ? Drill.ToString() : null)
            .Add(p => p.Label, "product")
            .Add(p => p.Options, [new FieldOption(Drill.ToString(), "Cordless drill")]);
    }

    private sealed class FakeSearch : ISearch
    {
        public List<SearchQuery> Queries { get; } = [];

        public Task<Result<SearchResults>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IReadOnlyList<SearchHit> hits = query.Text == "dri" ? [new SearchHit(Drill, "product", EntryState.Published, 2, 2, 1.0, null)] : [];
            return Task.FromResult(Result<SearchResults>.Success(new SearchResults(hits)));
        }
    }

    private sealed class FakeEntries : IEntryDirectory
    {
        public Task<Result<Entry>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var fields = JsonDocument.Parse("""{"name": {"value": "Cordless drill", "source": null}}""").RootElement;
            return Task.FromResult(Result<Entry>.Success(FluentCMS.Core.Content.Entry.Restore(id, Guid.NewGuid(), Guid.NewGuid(), "product", 2, EntryState.Published, new EntryVersion(id, 2, 1, fields, DateTimeOffset.UnixEpoch))));
        }

        public Task<Result<IReadOnlyList<EntrySummary>>> ListAsync(string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<EntryVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<EntryVersionSummary>>> HistoryAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Entry>> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Entry>> UpdateAsync(Guid id, int baseVersion, JsonElement fields, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid id, int baseVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<Entry>> RestoreAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeletedEntry>> ListDeletedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMedia : IMediaLibrary
    {
        public long MaxBytes => 50 * 1024 * 1024;

        public Task<IReadOnlyList<MediaAssetSummary>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<MediaAssetSummary> assets =
            [
                new(Hero, 1, null, "hero.jpg", "image/jpeg", 10, "products", [], true),
                new(Guid.NewGuid(), 1, null, "manual.pdf", "application/pdf", 10, "", [], false),
            ];
            return Task.FromResult(assets);
        }

        public Task<Result<MediaAsset>> UploadAsync(NewAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAsset>> UpdateAsync(Guid id, int baseVersion, AssetMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAsset>> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAssetVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaContent>> OpenAsync(Guid id, int? version = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result> DeleteAsync(Guid id, int baseVersion, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAsset>> RestoreAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeletedAsset>> ListDeletedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
