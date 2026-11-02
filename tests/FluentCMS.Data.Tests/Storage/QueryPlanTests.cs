using System.Data;
using FluentCMS.Core.Content.Query;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Tests.Schema;
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

    [Fact]
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

    [Fact]
    public void ABlockFilterIsAFullScanOfTheSharedTable()
    {
        using var database = new QueryDatabase();

        var rows = database.Engine().Compile("article", BracketQueryParser.Parse("filter[sections][hasBlock]=hero&locale=en")).Rows;

        Plan(rows).ShouldContain("SCAN");
    }

    [Fact]
    public void WhatReferencesATargetUsesTheTargetIndex()
    {
        using var database = new QueryDatabase();
        var target = Guid.CreateVersion7();

        var references = database.Context.References.Where(r => r.TargetType == "article" && r.TargetId == target);

        Plan(references).ShouldContain("ix_cms_reference_target");
    }
}
