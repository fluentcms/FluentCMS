using System.Data;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
using FluentCMS.Data.Tests.Providers;
using FluentCMS.Data.Tests.Query;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace FluentCMS.Data.Tests.Storage;

// What SQLite does with the indexes of the real model, read from EXPLAIN QUERY PLAN.
public class QueryPlanTests
{
    private const string SlugIndex = "ux_cms_article_i18n_slug";

    internal static string Plan(IQueryable query)
    {
        using var command = query.CreateDbCommand();
        command.CommandText = $"EXPLAIN QUERY PLAN {command.CommandText}";
        // A file database opens its connection on first use.
        if (command.Connection!.State != ConnectionState.Open)
        {
            command.Connection.Open();
        }

        using var reader = command.ExecuteReader();

        var steps = new List<string>();
        while (reader.Read())
        {
            steps.Add(reader.GetString(3));
        }

        return string.Join('\n', steps);
    }

    [SqliteFact("SQLite's query plans")]
    public void ASlugLookupUsesThePartialIndexForAPublishedStateHoweverItIsWritten()
    {
        using var database = new StorageDatabase(SampleSchema.FromClasses().Build());
        using var context = database.CreateCreatedContext();
        var site = Guid.CreateVersion7();
        short published = ContentState.Published;
        short draft = ContentState.Draft;

        var rows = context.Rows("cms_article_i18n");
        var literal = rows.Where(r => EF.Property<Guid>(r, "site_id") == site && EF.Property<string>(r, "locale") == "en" && EF.Property<string>(r, "slug") == "hello" && EF.Property<short>(r, "state") == 1);
        var inlined = rows.Where(r => EF.Property<Guid>(r, "site_id") == site && EF.Property<string>(r, "locale") == "en" && EF.Property<string>(r, "slug") == "hello" && EF.Property<short>(r, "state") == EF.Constant(published));
        var bound = rows.Where(r => EF.Property<Guid>(r, "site_id") == site && EF.Property<string>(r, "locale") == "en" && EF.Property<string>(r, "slug") == "hello" && EF.Property<short>(r, "state") == published);
        var boundDraft = rows.Where(r => EF.Property<Guid>(r, "site_id") == site && EF.Property<string>(r, "locale") == "en" && EF.Property<string>(r, "slug") == "hello" && EF.Property<short>(r, "state") == draft);

        Plan(literal).ShouldContain(SlugIndex);
        Plan(inlined).ShouldContain(SlugIndex);
        Plan(bound).ShouldContain(SlugIndex);
        Plan(boundDraft).ShouldNotContain(SlugIndex);
    }

    [SqliteFact("SQLite's query plans")]
    public void ABlockFilterIsAFullScanOfTheSharedTable()
    {
        using var database = new QueryDatabase();

        var rows = database.Engine().Compile("article", BracketQueryParser.Parse("filter[sections][hasBlock]=hero&locale=en")).Rows;

        Plan(rows).ShouldContain("SCAN");
    }

    // Without the state index each of these read cms_document of every type in id order.
    [SqliteFact("SQLite's query plans")]
    public void TheDefaultListSeeksTheStateIndexOfItsType()
    {
        using var database = new QueryDatabase();

        string List(string type, string query) => Plan(database.Engine().Compile(type, BracketQueryParser.Parse(query)).Rows);

        var author = List("author", $"site={QueryDatabase.SiteA}");
        author.ShouldContain("ix_cms_author_state (site_id=? AND state=?)");
        author.ShouldNotContain("SCAN");

        var article = List("article", $"locale=en&site={QueryDatabase.SiteA}");
        article.ShouldContain("ix_cms_article_i18n_state (site_id=? AND locale=? AND state=?)");
        article.ShouldNotContain("SCAN");

        var tag = List("tag", "");
        tag.ShouldContain("ix_cms_tag_state (state=?)");
        tag.ShouldNotContain("SCAN");
    }

    // As TrashPurgeJob pages through a type's trash: the first page, then the pages after the last entry read.
    [SqliteFact("SQLite's query plans")]
    public void TheTrashPurgeSeeksTheTrashOfItsType()
    {
        using var database = new QueryDatabase();
        var before = DateTimeOffset.UtcNow;
        var lastAt = before.AddDays(-1);
        var lastId = Guid.CreateVersion7();

        var due = database.Context.Documents.IgnoreQueryFilters().AsNoTracking()
            .Where(document => document.Type == "article" && document.DeletedAt != null && document.DeletedAt < before);
        var after = due.Where(document => document.DeletedAt > lastAt || (document.DeletedAt == lastAt && document.DocumentId.CompareTo(lastId) > 0));

        foreach (var rows in new[] { due, after })
        {
            var page = rows
                .OrderBy(document => document.DeletedAt).ThenBy(document => document.DocumentId)
                .Select(document => new { document.DocumentId, document.SiteId, At = document.DeletedAt!.Value })
                .Take(100);

            var plan = Plan(page);
            plan.ShouldContain("ix_cms_document_trash (type=? AND deleted_at>? AND deleted_at<?)");
            plan.ShouldNotContain("SCAN");
        }
    }

    // As EntryStore.AnyEntryAsync asks before a singleton is created.
    [SqliteFact("SQLite's query plans")]
    public void TheSingletonCheckSeeksTypeAndSite()
    {
        using var database = new QueryDatabase();
        var site = (Guid?)QueryDatabase.SiteA;
        var documents = database.Context.Documents.IgnoreQueryFilters();

        Plan(documents.Where(document => document.Type == "settings" && document.SiteId == site)).ShouldContain("ix_cms_document_type_site (type=? AND site_id=?)");
        Plan(documents.Where(document => document.Type == "brand")).ShouldContain("ix_cms_document_type_site (type=?)");
    }

    [SqliteFact("SQLite's query plans")]
    public void WhatReferencesATargetUsesTheTargetIndex()
    {
        using var database = new QueryDatabase();
        var target = Guid.CreateVersion7();

        var references = database.Context.References.Where(r => r.TargetType == "article" && r.TargetId == target);

        Plan(references).ShouldContain("ix_cms_reference_target");
    }
}
