using FluentCMS.Content;
using FluentCMS.Core.Api.Management;
using FluentCMS.Core.Api.OpenApi;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Api.Management;

// A route accepts and reads only the query parameters it declares with Query, which are the ones the OpenAPI document
// describes. Reading one it does not declare fails every request to the route, so no test of the route can pass.
public sealed class ManagementQueryTests
{
    private static readonly QueryParameter Search = QueryParameter.Text("search", "A term.");

    [Fact]
    public void ARouteAcceptsTheParametersItDeclaresAndThoseOfTheBridge()
    {
        var request = Request("?search=ada&page=2&site=main&locale=en", [Search, .. ManagementQuery.Paging]);

        Should.NotThrow(() => ManagementQuery.Only(request));
        ManagementQuery.Text(request, "search").ShouldBe("ada");
        ManagementQuery.Page(request).ShouldBe((2, ManagementQuery.DefaultPageSize));
    }

    [Fact]
    public void AParameterTheRouteDoesNotDeclareIsRefused()
    {
        var request = Request("?search=ada&status=pending", Search);

        var refused = Should.Throw<ValidationException>(() => ManagementQuery.Only(request));

        refused.Errors.ShouldHaveSingleItem().Path.ShouldBe("status");
    }

    [Fact]
    public void ReadingAParameterTheRouteDoesNotDeclareFailsWhetherTheRequestSendsItOrNot()
    {
        foreach (var query in new[] { "", "?search=ada" })
        {
            var request = Request(query, ManagementQuery.Paging);

            Should.Throw<InvalidOperationException>(() => ManagementQuery.Text(request, "search")).Message.ShouldContain("'search'");
        }

        Should.Throw<InvalidOperationException>(() => ManagementQuery.Page(Request("", Search)));
    }

    private static HttpRequest Request(string query, params QueryParameter[] declared)
    {
        DefaultHttpContext context = new();
        context.Request.QueryString = new QueryString(query);
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(declared), "test"));
        return context.Request;
    }
}
