using System.Text.Json.Nodes;
using Shouldly;

namespace FluentCMS.Data.Tests.Providers;

// A provider may keep JSON in a form of its own, as jsonb does with its key order and spacing, so a test reads stored JSON as
// the value it holds, never as its text.
internal static class StoredJson
{
    public static JsonNode Parse(object? stored) => JsonNode.Parse(stored.ShouldBeOfType<string>())!;

    public static void ShouldBeJson(this object? stored, string expected) =>
        JsonNode.DeepEquals(Parse(stored), JsonNode.Parse(expected)).ShouldBeTrue($"{stored} should hold {expected}");

    public static void ShouldBeJson(this JsonNode? stored, string expected) =>
        JsonNode.DeepEquals(stored, JsonNode.Parse(expected)).ShouldBeTrue($"{stored?.ToJsonString()} should hold {expected}");
}
