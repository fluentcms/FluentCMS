using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.Users;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the users area held to the reference host's OpenAPI document (spec 7), with the members of the two bodies the
// pages send: the forced password change of PATCH /users/{id} and the address and roles of POST /invitations.
public sealed class UsersContractTests
{
    private const string Prefix = "/api/management/v1/";

    [Fact]
    public async Task TheRecordsOfTheUsersAreaMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var users = UsersJsonContext.Default;
        List<string> gaps = [];

        void Check(System.Text.Json.Serialization.Metadata.JsonTypeInfo record, string path, string method, string status = "200") =>
            OpenApiContractTests.Check(record, OpenApiContractTests.Answer(paths, Prefix + path, method, status), $"{path} {method}", document, gaps);

        Check(users.UserRecordPage, "users", "get");
        Check(users.UserRecord, "users/{id}", "get");
        Check(users.UserRecord, "users/{id}", "patch");
        Check(users.UserRecord, "users/{id}/deactivate", "post");
        Check(users.UserRecord, "users/{id}/reactivate", "post");
        Check(users.UserRecord, "users/{id}/roles/{roleId}", "put");
        Check(users.UserRecord, "users/{id}/roles/{roleId}", "delete");
        Check(users.UserRecord, "users/{id}/two-factor/reset", "post");
        Check(users.ItemListUserSessionRecord, "users/{id}/sessions", "get");
        Check(users.ItemListUserLoginRecord, "users/{id}/logins", "get");
        Check(users.ItemListInvitationRecord, "invitations", "get");
        Check(users.InvitationSentRecord, "invitations", "post", "201");

        gaps.ShouldBeEmpty();

        // The search the list sends, the routes that answer no content, and the members of the bodies the pages send.
        Parameters(paths, "users", "get").ShouldContain("search");
        (string Path, string Method)[] empty = [("users/{id}/sessions/{sessionId}", "delete"), ("users/{id}/logins/{provider}", "delete"), ("invitations/{id}", "delete")];
        foreach (var (path, method) in empty)
        {
            paths[Prefix + path]?[method]?["responses"]?["204"].ShouldNotBeNull($"{method} {path}");
        }

        Body(document, "users/{id}", "patch").ContainsKey("mustChangePassword").ShouldBeTrue();
        var invitation = Body(document, "invitations", "post");
        invitation.ContainsKey("email").ShouldBeTrue();
        invitation.ContainsKey("roleIds").ShouldBeTrue();
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
