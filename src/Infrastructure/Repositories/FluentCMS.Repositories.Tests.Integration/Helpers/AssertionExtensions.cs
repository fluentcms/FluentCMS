using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.TestEntities;

namespace FluentCMS.Repositories.Tests.Integration.Helpers;

public static class AssertionExtensions
{
    #region TestUser Assertions

    public static void ShouldBeEquivalentToTestUser(this TestUser actual, TestUser expected)
    {
        actual.Should().NotBeNull();
        actual.Id.Should().Be(expected.Id);
        actual.Name.Should().Be(expected.Name);
        actual.Email.Should().Be(expected.Email);
        actual.Age.Should().Be(expected.Age);
    }

    public static void ShouldHaveValidGeneratedId(this TestUser user)
    {
        user.Should().NotBeNull();
        user.Id.Should().NotBe(Guid.Empty);
    }

    public static void ShouldBeOrderedByName(this IEnumerable<TestUser> users)
    {
        var userList = users.ToList();
        userList.Should().BeInAscendingOrder(u => u.Name);
    }

    public static void ShouldBeOrderedByNameDescending(this IEnumerable<TestUser> users)
    {
        var userList = users.ToList();
        userList.Should().BeInDescendingOrder(u => u.Name);
    }

    public static void ShouldBeOrderedByAge(this IEnumerable<TestUser> users)
    {
        var userList = users.ToList();
        userList.Should().BeInAscendingOrder(u => u.Age);
    }

    public static void ShouldBeOrderedByAgeDescending(this IEnumerable<TestUser> users)
    {
        var userList = users.ToList();
        userList.Should().BeInDescendingOrder(u => u.Age);
    }

    #endregion

    #region TestProduct Assertions

    public static void ShouldBeEquivalentToTestProduct(this TestProduct actual, TestProduct expected)
    {
        actual.Should().NotBeNull();
        actual.Id.Should().Be(expected.Id);
        actual.Name.Should().Be(expected.Name);
        actual.Price.Should().Be(expected.Price);
        actual.CategoryId.Should().Be(expected.CategoryId);
        actual.IsActive.Should().Be(expected.IsActive);
    }

    public static void ShouldHaveValidGeneratedId(this TestProduct product)
    {
        product.Should().NotBeNull();
        product.Id.Should().NotBe(Guid.Empty);
    }

    public static void ShouldBeOrderedByPrice(this IEnumerable<TestProduct> products)
    {
        var productList = products.ToList();
        productList.Should().BeInAscendingOrder(p => p.Price);
    }

    public static void ShouldBeOrderedByPriceDescending(this IEnumerable<TestProduct> products)
    {
        var productList = products.ToList();
        productList.Should().BeInDescendingOrder(p => p.Price);
    }

    public static void ShouldAllBeActive(this IEnumerable<TestProduct> products)
    {
        products.Should().OnlyContain(p => p.IsActive);
    }

    public static void ShouldAllBeInactive(this IEnumerable<TestProduct> products)
    {
        products.Should().OnlyContain(p => !p.IsActive);
    }

    #endregion

    #region TestCategory Assertions

    public static void ShouldBeEquivalentToTestCategory(this TestCategory actual, TestCategory expected)
    {
        actual.Should().NotBeNull();
        actual.Id.Should().Be(expected.Id);
        actual.Name.Should().Be(expected.Name);
        actual.Description.Should().Be(expected.Description);
    }

    public static void ShouldHaveValidGeneratedId(this TestCategory category)
    {
        category.Should().NotBeNull();
        category.Id.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region Collection Assertions

    public static void ShouldHaveCount<T>(this IEnumerable<T> collection, int expectedCount)
    {
        collection.Should().HaveCount(expectedCount);
    }

    public static void ShouldBeEmpty<T>(this IEnumerable<T> collection)
    {
        collection.Should().BeEmpty();
    }

    public static void ShouldNotBeEmpty<T>(this IEnumerable<T> collection)
    {
        collection.Should().NotBeEmpty();
    }

    public static void ShouldContainEntity<T>(this IEnumerable<T> collection, T entity) where T : class
    {
        collection.Should().Contain(entity);
    }

    public static void ShouldNotContainEntity<T>(this IEnumerable<T> collection, T entity) where T : class
    {
        collection.Should().NotContain(entity);
    }

    #endregion

    #region Pagination Assertions

    public static void ShouldHavePage<T>(this IPagedResult<T> pagedResult, int expectedPage)
    {
        pagedResult.Should().NotBeNull();
        pagedResult.Page.Should().Be(expectedPage);
    }

    public static void ShouldHavePageSize<T>(this IPagedResult<T> pagedResult, int expectedPageSize)
    {
        pagedResult.Should().NotBeNull();
        pagedResult.PageSize.Should().Be(expectedPageSize);
    }

    public static void ShouldHaveTotalCount<T>(this IPagedResult<T> pagedResult, long expectedTotalCount)
    {
        pagedResult.Should().NotBeNull();
        pagedResult.TotalCount.Should().Be(expectedTotalCount);
    }

    public static void ShouldHaveItems<T>(this IPagedResult<T> pagedResult, int expectedItemCount)
    {
        pagedResult.Should().NotBeNull();
        pagedResult.Items.Should().HaveCount(expectedItemCount);
    }

    #endregion

    #region Exception Assertions

    public static async Task ShouldThrowArgumentNullException(this Func<Task> action, string parameterName)
    {
        await action.Should().ThrowAsync<ArgumentNullException>()
            .WithMessage($"*{parameterName}*");
    }

    public static async Task ShouldThrowRepositoryException<T>(this Func<Task> action) where T : class, IEntity
    {
        await action.Should().ThrowAsync<RepositoryException<T>>();
    }

    public static async Task ShouldThrowOperationCancelledException(this Func<Task> action)
    {
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
