using FluentCMS.Core.Agents;

namespace FluentCMS.Tests.Agents;

public sealed class AgentScopeTests
{
    private static readonly Guid Site = TestAgentCredentials.Site;

    [Fact]
    public void A_scope_keeps_its_lists_once_each_in_order()
    {
        var other = Guid.NewGuid();

        var scope = AgentScope.Create(
            [Site, other, Site],
            ["product", "category", "product"],
            ["product.price", "product.currency", "product.price"],
            [AgentOperation.Update, AgentOperation.Read, AgentOperation.Update]).Value;

        Assert.Equal([Site, other], scope.Sites);
        Assert.Equal(["product", "category"], scope.Types);
        Assert.Equal(["product.price", "product.currency"], scope.Fields);
        Assert.Equal([AgentOperation.Update, AgentOperation.Read], scope.Operations);
    }

    [Fact]
    public void Leaving_types_and_fields_out_allows_all_of_them()
    {
        var scope = AgentScope.Create([Site], null, null, [AgentOperation.Read]).Value;

        Assert.Null(scope.Types);
        Assert.Null(scope.Fields);
        Assert.True(scope.AllowsType("product"));
        Assert.True(scope.AllowsField("product", "price"));
        Assert.True(scope.AllowsField("anything-else", "title"));
    }

    [Fact]
    public void Sites_and_operations_allow_only_what_is_listed()
    {
        var scope = TestAgentCredentials.Scope();

        Assert.True(scope.AllowsSite(Site));
        Assert.False(scope.AllowsSite(Guid.NewGuid()));
        Assert.True(scope.AllowsOperation(AgentOperation.Read));
        Assert.True(scope.AllowsOperation(AgentOperation.Update));
        Assert.False(scope.AllowsOperation(AgentOperation.Create));
        Assert.False(scope.AllowsOperation(AgentOperation.Delete));
    }

    [Fact]
    public void Listed_fields_narrow_only_the_types_they_name()
    {
        var scope = AgentScope.Create([Site], ["product", "category"], ["product.price", "product.currency"], [AgentOperation.Update]).Value;

        Assert.True(scope.AllowsType("product"));
        Assert.True(scope.AllowsType("category"));
        Assert.False(scope.AllowsType("region"));

        Assert.True(scope.AllowsField("product", "price"));
        Assert.True(scope.AllowsField("product", "currency"));
        Assert.False(scope.AllowsField("product", "name"));
        Assert.True(scope.AllowsField("category", "name"));
        Assert.False(scope.AllowsField("region", "name"));
    }

    [Fact]
    public void Fields_without_a_type_list_narrow_their_types_and_leave_the_rest_open()
    {
        var scope = AgentScope.Create([Site], null, ["product.price"], [AgentOperation.Update]).Value;

        Assert.True(scope.AllowsField("product", "price"));
        Assert.False(scope.AllowsField("product", "name"));
        Assert.True(scope.AllowsField("category", "name"));
    }

    [Fact]
    public void Names_are_matched_exactly()
    {
        var scope = TestAgentCredentials.Scope();

        Assert.False(scope.AllowsType("Product"));
        Assert.False(scope.AllowsField("product", "Price"));
    }

    [Fact]
    public void Every_problem_with_a_scope_is_reported_at_once()
    {
        var result = AgentScope.Create(
            [Guid.Empty],
            ["product", "Not A Slug"],
            ["product.price", "price", "product.", "Product.price", "category.name"],
            [AgentOperation.Read, (AgentOperation)42]);

        Assert.Equal(
            [
                AgentCredentialErrorCodes.ScopeSiteInvalid,
                AgentCredentialErrorCodes.ScopeTypeInvalid,
                AgentCredentialErrorCodes.ScopeFieldInvalid,
                AgentCredentialErrorCodes.ScopeFieldInvalid,
                AgentCredentialErrorCodes.ScopeFieldInvalid,
                AgentCredentialErrorCodes.ScopeFieldOutsideTypes,
                AgentCredentialErrorCodes.ScopeOperationInvalid,
            ],
            result.Errors.Select(error => error.Code));
        Assert.Contains("'Not A Slug'", result.Errors[1].Message);
        Assert.Contains("'category'", result.Errors[5].Message);
    }

    [Fact]
    public void Empty_lists_are_refused_rather_than_read_as_nothing_or_everything()
    {
        var result = AgentScope.Create([], [], [], []);

        Assert.Equal(
            [
                AgentCredentialErrorCodes.ScopeSitesMissing,
                AgentCredentialErrorCodes.ScopeTypesEmpty,
                AgentCredentialErrorCodes.ScopeFieldsEmpty,
                AgentCredentialErrorCodes.ScopeOperationsMissing,
            ],
            result.Errors.Select(error => error.Code));
    }

    [Fact]
    public void Restore_keeps_a_stored_scope_without_checking_it()
    {
        var scope = AgentScope.Restore([], [], ["not a field"], []);

        Assert.Empty(scope.Sites);
        Assert.Equal(["not a field"], scope.Fields);
    }
}
