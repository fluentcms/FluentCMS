using System.Text.Json.Nodes;
using FluentCMS.Admin.Administration.ApiKeys;
using FluentCMS.Admin.Api;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Sites;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records and the calls of the API keys pages (spec 4.15), held to the reference host's OpenAPI document and run against
// the host as the pages make them.
public sealed class ApiKeysContractTests
{
    private static CancellationToken Token => AdminHost.Token;

    [Fact]
    public async Task TheRecordsOfTheKeysPagesMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        var paths = document["paths"]!;
        var keys = ApiKeysJsonContext.Default;
        List<string> gaps = [];

        OpenApiContractTests.Check(keys.ItemListApiKeyInfo, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys", "get"), "api-keys", document, gaps);
        OpenApiContractTests.Check(keys.ApiKeyInfo, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys/{id}", "get"), "api-keys/{id}", document, gaps);
        OpenApiContractTests.Check(keys.ApiKeySecret, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys", "post", "201"), "api-keys post", document, gaps);
        OpenApiContractTests.Check(keys.ApiKeySecret, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys/{id}/rotate", "post"), "api-keys/{id}/rotate", document, gaps);
        OpenApiContractTests.Check(keys.ApiKeyInfo, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys/{id}/expire", "post"), "api-keys/{id}/expire", document, gaps);
        OpenApiContractTests.Check(keys.ApiKeyInfo, OpenApiContractTests.Answer(paths, "/api/management/v1/api-keys/{id}/allowed-addresses", "put"), "api-keys/{id}/allowed-addresses", document, gaps);
        paths["/api/management/v1/api-keys/{id}"]!["delete"].ShouldNotBeNull();

        // The members the pages send are the ones the bodies take.
        Body(document, "/api/management/v1/api-keys", "post").ShouldBe(["name", "class", "roleId", "siteIds", "typeNames", "expiresAt", "rateLimit", "allowedAddresses"], ignoreOrder: true);
        Body(document, "/api/management/v1/api-keys/{id}/rotate", "post").ShouldBe(["overlapSeconds"]);
        Body(document, "/api/management/v1/api-keys/{id}/expire", "post").ShouldBe(["at"]);
        Body(document, "/api/management/v1/api-keys/{id}/allowed-addresses", "put").ShouldBe(["allowedAddresses"]);

        gaps.ShouldBeEmpty();
    }

    // Acceptance: a caller who holds apikeys.manage in one site sees that site's keys only, a key bound to no site answers it
    // 404, and a change of a key whose role it is below answers 403, which the key's page names.
    [Fact]
    public async Task ACallerWhoManagesKeysInOneSiteSeesThatSitesKeysAndIsRefusedAKeyAboveItsRole()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var everywhere = await super.Api.CreateApiKeyAsync(new ApiKeyDraft("Every site", "delivery", BuiltInRoles.Editor), Token);
        var inSite = await super.Api.CreateApiKeyAsync(new ApiKeyDraft("Default site", "delivery", BuiltInRoles.Editor) { SiteIds = [DefaultSite.Id] }, Token);
        inSite.Secret.ShouldNotBeNullOrWhiteSpace();
        (await super.Api.GetApiKeysAsync(Token)).Select(key => key.Id).ShouldBe([everywhere.Id, inSite.Id], ignoreOrder: true);

        var role = new JsonObject
        {
            ["name"] = "Key keeper",
            ["siteId"] = DefaultSite.Id,
            ["grants"] = new JsonArray(new JsonObject { ["permission"] = CorePermissions.ApiKeysManage }),
        };
        await super.Api.SendAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = role }, Token);
        using var keeper = await admin.SignInAsync(await admin.Host.CreateUserAsync("Key keeper", cancellationToken: Token));

        (await keeper.Api.GetApiKeysAsync(Token)).Select(key => key.Name).ShouldBe(["Default site"]);
        var hidden = await Should.ThrowAsync<ApiProblemException>(() => keeper.Api.GetApiKeyAsync(everywhere.Id, Token));
        ApiKeyProblems.IsNotFound(hidden.Problem).ShouldBeTrue();

        var refused = await Should.ThrowAsync<ApiProblemException>(() => keeper.Api.RotateApiKeyAsync(inSite.Id, 0, Token));
        ApiKeyProblems.IsRefused(refused.Problem).ShouldBeTrue();
        refused = await Should.ThrowAsync<ApiProblemException>(() => keeper.Api.RevokeApiKeyAsync(inSite.Id, Token));
        ApiKeyProblems.IsRefused(refused.Problem).ShouldBeTrue();
    }

    // Acceptance: rotated with an overlap, expired and revoked, each read back as the key's page and the list show it.
    [Fact]
    public async Task AKeyIsRotatedWithAnOverlapExpiredAndRevoked()
    {
        await using var admin = new AdminHost();
        using var super = await admin.SignInAsync(BuiltInRoles.SuperAdminName);
        var created = await super.Api.CreateApiKeyAsync(
            new ApiKeyDraft("Rotated", "delivery", BuiltInRoles.Editor) { SiteIds = [DefaultSite.Id], AllowedAddresses = ["203.0.113.0/24"], RateLimit = 60 },
            Token);

        var rotated = await super.Api.RotateApiKeyAsync(created.Id, 3600, Token);
        rotated.Secret.ShouldNotBe(created.Secret);
        var key = await super.Api.GetApiKeyAsync(created.Id, Token);
        key.PreviousKeyExpiresAt.ShouldNotBeNull();
        key.AllowedAddresses.ShouldBe(["203.0.113.0/24"]);

        // An overlap past the server's longest is refused on its field.
        var tooLong = await Should.ThrowAsync<ApiProblemException>(() => super.Api.RotateApiKeyAsync(created.Id, int.MaxValue, Token));
        ApiKeyProblems.HasFieldErrors(tooLong.Problem, "overlapSeconds").ShouldBeTrue();

        (await super.Api.SetApiKeyAddressesAsync(created.Id, null, Token)).AllowedAddresses.ShouldBeNull();
        var later = DateTimeOffset.UtcNow.AddDays(1);
        (await super.Api.ExpireApiKeyAsync(created.Id, later, Token)).ExpiresAt.ShouldNotBeNull();
        var notForward = await Should.ThrowAsync<ApiProblemException>(() => super.Api.ExpireApiKeyAsync(created.Id, later.AddDays(1), Token));
        ApiKeyProblems.HasFieldErrors(notForward.Problem, "expiresAt").ShouldBeTrue();
        var expired = await super.Api.ExpireApiKeyAsync(created.Id, null, Token);
        expired.Expired(DateTimeOffset.UtcNow.AddSeconds(1)).ShouldBeTrue();

        await super.Api.RevokeApiKeyAsync(created.Id, Token);
        (await super.Api.GetApiKeysAsync(Token)).Single(found => found.Id == created.Id).Revoked.ShouldBeTrue();
    }

    private static IEnumerable<string> Body(JsonObject document, string path, string method)
    {
        var schema = document["paths"]![path]![method]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            schema = document["components"]!["schemas"]![reference.Split('/')[^1]]!;
        }

        return schema["properties"]!.AsObject().Select(property => property.Key);
    }
}
