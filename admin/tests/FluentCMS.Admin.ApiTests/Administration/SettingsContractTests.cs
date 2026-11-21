using FluentCMS.Admin.Administration.Settings;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the settings pages held to the reference host's OpenAPI document (spec 7): the menu's list, the read of
// an area and the answer of a write, for the site and for the instance defaults, and the If-Match a write and a reset send.
public sealed class SettingsContractTests
{
    [Fact]
    public async Task TheRecordsOfTheSettingsPagesMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        List<string> gaps = [];

        OpenApiContractTests.Check(SettingsJsonContext.Default.ItemListSettingsAreaInfo, OpenApiContractTests.Answer(paths, "/api/management/v1/settings", "get"), "settings", document, gaps);
        foreach (var path in (string[])["/api/management/v1/settings/{area}", "/api/management/v1/settings/{area}/defaults"])
        {
            OpenApiContractTests.Check(SettingsJsonContext.Default.SettingsRead, OpenApiContractTests.Answer(paths, path, "get"), path, document, gaps);
            OpenApiContractTests.Check(SettingsJsonContext.Default.SettingsRead, OpenApiContractTests.Answer(paths, path, "put"), $"{path} put", document, gaps);
            paths[path]!["delete"]!["responses"]!["204"].ShouldNotBeNull(path);
            foreach (var method in (string[])["put", "delete"])
            {
                paths[path]![method]!["parameters"]!.AsArray()
                    .Select(parameter => parameter!["$ref"]?.GetValue<string>() ?? parameter["name"]?.GetValue<string>())
                    .ShouldContain(name => name != null && name.Contains("if-match", StringComparison.OrdinalIgnoreCase), $"{method} {path}");
            }
        }

        gaps.ShouldBeEmpty();
    }
}
