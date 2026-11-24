using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Diagnostics;

// The diagnostics area against a fake of GET /system/diagnostics and GET /system/health, with the report of a host in
// Production that has an insecure default, a pending migration and a schema that drifted, and one check that is degraded.
// A refusal is given with Management.Answer, which is asked first.
public abstract class DiagnosticsTestContext : AdministrationTestContext
{
    protected DiagnosticsTestContext() => Management.Respond = Answer;

    internal JsonObject Report { get; } = new()
    {
        ["startedAt"] = "2026-11-22T08:00:00+00:00",
        ["environment"] = "Production",
        ["version"] = "1.4.0",
        ["failure"] = null,
        ["dataProvider"] = new JsonObject { ["provider"] = "Sqlite", ["providerVersion"] = "1.4.0", ["migrateOnStartup"] = true, ["schemaSync"] = "Off" },
        ["assemblies"] = new JsonArray(new JsonObject { ["name"] = "FluentCMS.Core", ["version"] = "1.4.0" }),
        ["plugins"] = new JsonArray(new JsonObject { ["name"] = "search", ["version"] = "1.0.0" }),
        ["providerSlots"] = new JsonArray(new JsonObject { ["slot"] = "ISearchIndex", ["implementation"] = "SearchIndex", ["source"] = "Plugin", ["plugin"] = "search" }),
        ["pendingMigrations"] = new JsonArray(new JsonObject { ["stream"] = "content", ["id"] = "20261120_AddReviews" }),
        ["schema"] = new JsonObject
        {
            ["hash"] = "abc123",
            ["appliedHash"] = "def456",
            ["appliedAt"] = "2026-11-20T08:00:00+00:00",
            ["appliedVersion"] = "1.3.0",
            ["drifted"] = true,
            ["changes"] = "+ review.rating",
        },
        ["insecureDefaults"] = new JsonArray(new JsonObject { ["id"] = "webhooks-private-networks", ["message"] = "Webhooks may deliver to private networks." }),
    };

    internal JsonObject Health { get; } = new()
    {
        ["status"] = "Degraded",
        ["totalDurationMs"] = 12.5,
        ["checks"] = new JsonArray(
            new JsonObject { ["name"] = "database", ["status"] = "Healthy", ["durationMs"] = 3.25, ["tags"] = new JsonArray("live", "ready"), ["description"] = null, ["error"] = null },
            new JsonObject { ["name"] = "outbox", ["status"] = "Degraded", ["durationMs"] = 9.0, ["tags"] = new JsonArray("ready"), ["description"] = "42 deliveries wait.", ["error"] = "TimeoutException: The drain is late." }),
    };

    internal static IElement Section(IRenderedComponent<AdminApp> app, string heading) =>
        app.FindAll("section.cms-card").Single(section => section.QuerySelector("h2")!.TextContent == heading);

    private HttpResponseMessage? Answer(HttpRequestMessage request, string path, string? body) => path switch
    {
        "system/diagnostics" => Json(Report.DeepClone()),
        "system/health" => Json(Health.DeepClone()),
        _ => null,
    };
}
