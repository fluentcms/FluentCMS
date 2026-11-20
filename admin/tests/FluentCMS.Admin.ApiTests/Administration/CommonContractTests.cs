using FluentCMS.Admin.Administration.Common;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests.Administration;

// The records of the administration assembly are its own (AQ6), held to the reference host's OpenAPI document as the
// shell's are (spec 7). Each area holds its records in a file of its own beside this one, with the same two helpers.
public sealed class CommonContractTests
{
    [Fact]
    public async Task TheRolesTheAreasReadMatchTheOpenApiDocument()
    {
        var document = await OpenApiDocument.ReadAsync();
        List<string> gaps = [];

        OpenApiContractTests.Check(
            CommonJsonContext.Default.ItemListRoleSummary,
            OpenApiContractTests.Answer(document["paths"]!, "/api/management/v1/roles", "get"),
            "roles",
            document,
            gaps);

        gaps.ShouldBeEmpty();
    }
}
