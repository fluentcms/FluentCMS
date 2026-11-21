using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Members;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the members area held to the reference host's OpenAPI document (spec 7), with the filters the list sends and
// the members of the bodies the pages send: a new member, its display name, and a group's name, description and role.
public sealed class MembersContractTests
{
    private const string Prefix = "/api/management/v1/";

    [Fact]
    public async Task TheRecordsOfTheMembersAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var members = MembersJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(members.MemberRecordPage, "members", "get");
        Check(members.MemberRecord, "members", "post", "201");
        Check(members.MemberRecord, "members/{id}", "get");
        Check(members.MemberRecord, "members/{id}", "patch");
        Check(members.MemberRecord, "members/{id}/deactivate", "post");
        Check(members.MemberRecord, "members/{id}/reactivate", "post");
        Check(members.MemberRecord, "members/{id}/groups/{groupId}", "put");
        Check(members.MemberRecord, "members/{id}/groups/{groupId}", "delete");
        Check(members.ItemListMemberSessionRecord, "members/{id}/sessions", "get");
        Check(members.ItemListMemberLoginRecord, "members/{id}/logins", "get");
        Check(members.ItemListMemberGroupRecord, "member-groups", "get");
        Check(members.MemberGroupRecord, "member-groups", "post", "201");
        Check(members.MemberGroupRecord, "member-groups/{id}", "get");
        Check(members.MemberGroupRecord, "member-groups/{id}", "put");

        gaps.ShouldBeEmpty();

        // The search and filters the list sends, the routes that answer no content, and the members of the bodies the pages send.
        var parameters = Parameters(paths, "members", "get");
        foreach (var name in (string[])[MembersApi.GroupFilter, MembersApi.VerifiedFilter, MembersApi.ActiveFilter, "search", "page", "pageSize"])
        {
            parameters.ShouldContain(name);
        }

        (string Path, string Method)[] empty =
            [("members/{id}", "delete"), ("members/{id}/sessions/{sessionId}", "delete"), ("members/{id}/logins/{provider}", "delete"), ("member-groups/{id}", "delete")];
        foreach (var (path, method) in empty)
        {
            paths[Prefix + path]?[method]?["responses"]?["204"].ShouldNotBeNull($"{method} {path}");
        }

        Body(document, "members", "post").Select(member => member.Key).ShouldBe(["email", "displayName"], ignoreOrder: true);
        Body(document, "members/{id}", "patch").Select(member => member.Key).ShouldBe(["displayName"]);
        Body(document, "member-groups", "post").Select(member => member.Key).ShouldBe(["name", "description", "roleId"], ignoreOrder: true);
        Body(document, "member-groups/{id}", "put").Select(member => member.Key).ShouldBe(["name", "description", "roleId"], ignoreOrder: true);
    }

    private static string[] Parameters(JsonNode paths, string path, string method) =>
        [.. paths[Prefix + path]![method]!["parameters"]!.AsArray().Select(parameter => parameter!["name"]?.GetValue<string>()).OfType<string>()];

    private static JsonObject Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![Prefix + path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        var reference = schema["$ref"]?.GetValue<string>();
        var resolved = reference is null ? schema : document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        return resolved["properties"]!.AsObject();
    }
}
