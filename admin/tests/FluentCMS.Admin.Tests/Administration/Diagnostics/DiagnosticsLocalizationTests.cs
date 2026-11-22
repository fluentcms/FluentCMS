using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Diagnostics;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Administration.Diagnostics;

// The check of spec 3.1 on the pages of the area: under a pseudo-culture no text is left untranslated. The area's issue
// adds each of its routes here, with the answers of the fake Management API that its page reads, so that the page is
// checked with its data and not only in its error state.
public sealed class DiagnosticsLocalizationTests : DiagnosticsTestContext
{
    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheDiagnosticsPageComesFromTheResources(string culture)
    {
        MarkData(culture);

        var app = LoadLocalized(culture, "system/diagnostics", shown =>
        {
            Section(shown, Localized<DiagnosticsResources>("Diagnostics.Report.Heading")).QuerySelector("dl").ShouldNotBeNull();
            Section(shown, Localized<DiagnosticsResources>("Diagnostics.Health.Heading")).QuerySelectorAll("tbody tr").Length.ShouldBe(2);
        });

        // The heading of an area's first page is {Area}.Page.Heading of its resources, which the end-to-end tests find it by.
        Heading(app).ShouldBe(Localized<DiagnosticsResources>("Diagnostics.Page.Heading"));
    }

    // Data the fake serves is marked as a resource is, so that only a text of the page itself can fail the check: what the
    // report and the checks say of the host, which the page shows as it is. A status of a check is a name the page knows.
    private void MarkData(string culture)
    {
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        string Mark(string value) => PseudoCulture.Transform(value, rightToLeft);
        void MarkAll(JsonNode? node, params string[] members)
        {
            foreach (var item in node is JsonArray array ? [.. array] : new[] { node })
            {
                foreach (var member in members)
                {
                    if (item![member] is JsonArray texts)
                    {
                        item[member] = new JsonArray([.. texts.Select(text => (JsonNode)Mark((string)text!))]);
                    }
                    else if (item[member] is { } value)
                    {
                        item[member] = Mark((string)value!);
                    }
                }
            }
        }

        MarkAll(Report, "environment", "version");
        MarkAll(Report["dataProvider"], "provider", "providerVersion", "schemaSync");
        MarkAll(Report["assemblies"], "name", "version");
        MarkAll(Report["plugins"], "name", "version");
        MarkAll(Report["providerSlots"], "slot", "implementation", "source", "plugin");
        MarkAll(Report["pendingMigrations"], "stream", "id");
        MarkAll(Report["schema"], "hash", "appliedHash", "appliedVersion", "changes");
        MarkAll(Report["insecureDefaults"], "id", "message");
        MarkAll(Health["checks"], "name", "tags", "description", "error");
    }
}
