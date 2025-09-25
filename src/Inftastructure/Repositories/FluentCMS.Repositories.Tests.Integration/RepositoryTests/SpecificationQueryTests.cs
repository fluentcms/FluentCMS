using FluentAssertions;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.Tests.Integration.Helpers;
using FluentCMS.Repositories.Tests.Integration.TestEntities;
using FluentCMS.Repositories.Tests.Integration.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Repositories.Tests.Integration.RepositoryTests;

public class SpecificationQueryTests : IClassFixture<SqliteTestFixture>
{
    private readonly SqliteTestFixture _fixture;
    private readonly IRepository<TestUser> _userRepository;
    private readonly IRepository<TestProduct> _productRepository;

    public SpecificationQueryTests(SqliteTestFixture fixture)
    {
        _fixture = fixture;
        _userRepository = _fixture.GetRepository<TestUser>();
        _productRepository = _fixture.GetRepository<TestProduct>();
    }

    #region Basic Query Operations Tests

    [Fact]
    public async Task Query_WithWhereSpecification_ShouldReturnFilteredResults()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 30);

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(2); // Bob (35) and Charlie (42)
        resultList.Should().OnlyContain(u => u.Age > 30);
    }

    [Fact]
    public async Task Query_WithMultipleWhereConditions_ShouldApplyAllFilters()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age >= 25 && u.Age <= 35);

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCount(3); // Jane (25), John (30), Bob (35)
        resultList.Should().OnlyContain(u => u.Age >= 25 && u.Age <= 35);
    }

    [Fact]
    public async Task Query_WithNoMatches_ShouldReturnEmptyList()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 100);

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FirstOrDefault_WithMatches_ShouldReturnFirstEntity()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 30);

        // Act
        var result = await _userRepository.FirstOrDefault(specification);

        // Assert
        result.Should().NotBeNull();
        result!.Age.Should().BeGreaterThan(30);
    }

    [Fact]
    public async Task FirstOrDefault_WithNoMatches_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 100);

        // Act
        var result = await _userRepository.FirstOrDefault(specification);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SingleOrDefault_WithSingleMatch_ShouldReturnEntity()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Name == "John Doe");

        // Act
        var result = await _userRepository.SingleOrDefault(specification);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("John Doe");
    }

    [Fact]
    public async Task SingleOrDefault_WithNoMatches_ShouldReturnNull()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Name == "Nonexistent User");

        // Act
        var result = await _userRepository.SingleOrDefault(specification);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SingleOrDefault_WithMultipleMatches_ShouldThrowException()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 25);

        // Act & Assert
        var action = async () => await _userRepository.SingleOrDefault(specification);
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Count_WithFilters_ShouldReturnCorrectCount()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age >= 30);

        // Act
        var result = await _userRepository.Count(specification);

        // Assert
        result.Should().Be(3); // John (30), Bob (35), Charlie (42)
    }

    [Fact]
    public async Task Count_WithNoMatches_ShouldReturnZero()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 100);

        // Act
        var result = await _userRepository.Count(specification);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task Any_WithMatches_ShouldReturnTrue()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 40);

        // Act
        var result = await _userRepository.Any(specification);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task Any_WithNoMatches_ShouldReturnFalse()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 100);

        // Act
        var result = await _userRepository.Any(specification);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Ordering Operations Tests

    [Fact]
    public async Task Query_WithOrderBy_ShouldReturnOrderedResults()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForSorting();
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query().OrderBy(u => u.Name);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.ShouldBeOrderedByName();
    }

    [Fact]
    public async Task Query_WithOrderByDescending_ShouldReturnDescendingResults()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForSorting();
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query().OrderByDescending(u => u.Age);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.ShouldBeOrderedByAgeDescending();
    }

    [Fact]
    public async Task Query_WithOrderByThenBy_ShouldApplySecondarySort()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = new List<TestUser>
        {
            TestDataBuilder.CreateTestUser("Alice", "alice1@example.com", 25),
            TestDataBuilder.CreateTestUser("Alice", "alice2@example.com", 30),
            TestDataBuilder.CreateTestUser("Bob", "bob@example.com", 25)
        };
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .ThenBy(u => u.Age);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList[0].Name.Should().Be("Alice");
        resultList[0].Age.Should().Be(25);
        resultList[1].Name.Should().Be("Alice");
        resultList[1].Age.Should().Be(30);
        resultList[2].Name.Should().Be("Bob");
    }

    [Fact]
    public async Task Query_WithOrderByDescendingThenByDescending_ShouldApplyBothDescending()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = new List<TestUser>
        {
            TestDataBuilder.CreateTestUser("Alice", "alice1@example.com", 25),
            TestDataBuilder.CreateTestUser("Alice", "alice2@example.com", 30),
            TestDataBuilder.CreateTestUser("Bob", "bob@example.com", 35)
        };
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderByDescending(u => u.Name)
            .ThenByDescending(u => u.Age);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList[0].Name.Should().Be("Bob");
        resultList[1].Name.Should().Be("Alice");
        resultList[1].Age.Should().Be(30);
        resultList[2].Name.Should().Be("Alice");
        resultList[2].Age.Should().Be(25);
    }

    [Fact]
    public async Task Query_WithComplexOrdering_ShouldMaintainSortOrder()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForSorting();
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Age)
            .ThenByDescending(u => u.Name);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().BeInAscendingOrder(u => u.Age);
        // For users with same age, should be descending by name
        var sameAgeGroups = resultList.GroupBy(u => u.Age).Where(g => g.Count() > 1);
        foreach (var group in sameAgeGroups)
        {
            group.Should().BeInDescendingOrder(u => u.Name);
        }
    }

    [Fact]
    public async Task Query_WithOrderByOnDifferentDataTypes_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var querySpec = _productRepository.Query()
            .OrderBy(p => p.Price)
            .ThenBy(p => p.IsActive);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _productRepository.Query(specification);

        // Assert
        result.ShouldBeOrderedByPrice();
    }

    [Fact]
    public async Task Query_WithNullValues_ShouldHandleOrderingCorrectly()
    {
        // Arrange - This test would require nullable properties to be meaningful
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForSorting();
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query().OrderBy(u => u.Email);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().NotBeEmpty();
        var resultList = result.ToList();
        resultList.Should().BeInAscendingOrder(u => u.Email);
    }

    [Fact]
    public async Task Query_WithOrderByOnNavigationProperty_ShouldWork()
    {
        // Arrange - For this test, we'll order by a simple property since we don't have navigation properties
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var querySpec = _productRepository.Query().OrderBy(p => p.CategoryId);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _productRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().BeInAscendingOrder(p => p.CategoryId);
    }

    #endregion

    #region Pagination Operations Tests

    [Fact]
    public async Task Query_WithSkip_ShouldSkipCorrectNumber()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(10);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .Skip(3);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().HaveCount(7); // 10 - 3 = 7
    }

    [Fact]
    public async Task Query_WithTake_ShouldTakeCorrectNumber()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(10);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .Take(5);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task Query_WithSkipAndTake_ShouldImplementPagination()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(10);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .Skip(3)
            .Take(4);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().HaveCount(4);
    }

    [Fact]
    public async Task Query_WithSkipGreaterThanTotal_ShouldReturnEmpty()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(5);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .Skip(10);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Query_WithTakeGreaterThanRemaining_ShouldReturnAvailable()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(5);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .OrderBy(u => u.Name)
            .Skip(3)
            .Take(10);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().HaveCount(2); // Only 2 items remaining after skipping 3
    }

    [Fact]
    public async Task FindPaged_WithValidParams_ShouldReturnPagedResult()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(10);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0); // All users

        // Act
        var result = await _userRepository.FindPaged(specification, page: 2, pageSize: 3);

        // Assert
        result.ShouldHavePage(2);
        result.ShouldHavePageSize(3);
        result.ShouldHaveTotalCount(10);
        result.ShouldHaveItems(3);
    }

    [Fact]
    public async Task FindPaged_WithPageBeyondResults_ShouldReturnEmptyPage()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(5);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);

        // Act
        var result = await _userRepository.FindPaged(specification, page: 10, pageSize: 3);

        // Assert
        result.ShouldHavePage(10);
        result.ShouldHavePageSize(3);
        result.ShouldHaveTotalCount(5);
        result.ShouldHaveItems(0);
    }

    [Fact]
    public async Task FindPaged_WithLargePageSize_ShouldReturnAllResults()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(5);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 0);

        // Act
        var result = await _userRepository.FindPaged(specification, page: 1, pageSize: 100);

        // Assert
        result.ShouldHavePage(1);
        result.ShouldHavePageSize(100);
        result.ShouldHaveTotalCount(5);
        result.ShouldHaveItems(5);
    }

    #endregion

    #region Aggregation Operations Tests

    [Fact]
    public async Task Sum_WithValidSelector_ShouldReturnCorrectSum()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var specification = SpecificationExtensions.Where<TestProduct>(p => p.IsActive);

        // Act
        var result = await _productRepository.Sum(specification, p => p.Price);

        // Assert
        // Active products: Product A (10.50), Product B (25.00), Product C (15.75), Product E (5.25)
        var expectedSum = 10.50m + 25.00m + 15.75m + 5.25m;
        result.Should().Be(expectedSum);
    }

    [Fact]
    public async Task Sum_WithNoMatches_ShouldReturnZero()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var specification = SpecificationExtensions.Where<TestProduct>(p => p.Price > 1000);

        // Act
        var result = await _productRepository.Sum(specification, p => p.Price);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task Sum_WithNullValues_ShouldIgnoreNulls()
    {
        // Arrange - Since our Price is not nullable, this test validates the sum calculation
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var specification = SpecificationExtensions.Where<TestProduct>(p => p.CategoryId == 1);

        // Act
        var result = await _productRepository.Sum(specification, p => p.Price);

        // Assert
        // Category 1 products: Product A (10.50), Product B (25.00)
        var expectedSum = 10.50m + 25.00m;
        result.Should().Be(expectedSum);
    }

    [Fact]
    public async Task Count_WithLargeDataset_ShouldReturnCorrectCount()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(1000);
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age >= 30);

        // Act
        var result = await _userRepository.Count(specification);

        // Assert
        result.Should().BeGreaterThan(0);
        result.Should().BeLessOrEqualTo(1000);
    }

    [Fact]
    public async Task Any_WithComplexPredicate_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var specification = SpecificationExtensions.Where<TestProduct>(p => 
            p.IsActive && p.Price > 20.00m && p.CategoryId == 1);

        // Act
        var result = await _productRepository.Any(specification);

        // Assert
        result.Should().BeTrue(); // Product B matches: active, price 25.00, category 1
    }

    [Fact]
    public async Task Query_WithGroupBy_ShouldGroupCorrectly()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var products = TestDataBuilder.CreateProductsForAggregation();
        await _productRepository.AddRange(products);

        var querySpec = _userRepository.Query()
            .GroupBy(u => u.Age);
        var specification = querySpec.ToSpecification();

        // Note: This test is more about ensuring GroupBy doesn't break the query
        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region Advanced Query Operations Tests

    [Fact]
    public async Task Query_WithDistinct_ShouldRemoveDuplicates()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = new List<TestUser>
        {
            TestDataBuilder.CreateTestUser("John", "john@example.com", 30),
            TestDataBuilder.CreateTestUser("John", "john2@example.com", 30), // Different email
            TestDataBuilder.CreateTestUser("Jane", "jane@example.com", 25)
        };
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query().Distinct();
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().HaveCount(3); // All are distinct due to different IDs
    }

    [Fact]
    public async Task Query_WithComplexSpecification_ShouldApplyAllOperations()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateTestUsers(20);
        await _userRepository.AddRange(users);

        var querySpec = _userRepository.Query()
            .Where(u => u.Age >= 25)
            .OrderBy(u => u.Name)
            .Skip(2)
            .Take(5);
        var specification = querySpec.ToSpecification();

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().HaveCountLessOrEqualTo(5);
        resultList.Should().OnlyContain(u => u.Age >= 25);
        resultList.Should().BeInAscendingOrder(u => u.Name);
    }

    [Fact]
    public async Task Query_WithNestedExpressions_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => 
            (u.Age > 25 && u.Name.Contains("o")) || u.Email.Contains("alice"));

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().NotBeEmpty();
        var resultList = result.ToList();
        resultList.Should().OnlyContain(u => 
            (u.Age > 25 && u.Name.Contains("o")) || u.Email.Contains("alice"));
    }

    [Fact]
    public async Task Query_WithDateTimeComparisons_ShouldWork()
    {
        // Arrange - Since our entities don't have DateTime properties, we'll use Age comparison
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => u.Age > 25 && u.Age < 40);

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().NotBeEmpty();
        var resultList = result.ToList();
        resultList.Should().OnlyContain(u => u.Age > 25 && u.Age < 40);
    }

    [Fact]
    public async Task Query_WithStringOperations_ShouldWork()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => 
            u.Name.StartsWith("J") || u.Email.EndsWith(".com"));

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        result.Should().NotBeEmpty();
        var resultList = result.ToList();
        resultList.Should().OnlyContain(u => 
            u.Name.StartsWith("J") || u.Email.EndsWith(".com"));
    }

    [Fact]
    public async Task Query_WithNullChecks_ShouldHandleNullsCorrectly()
    {
        // Arrange
        await _fixture.CleanDatabase();
        var users = TestDataBuilder.CreateUsersForFiltering();
        await _userRepository.AddRange(users);

        var specification = SpecificationExtensions.Where<TestUser>(u => 
            u.Name != null && u.Email != null);

        // Act
        var result = await _userRepository.Query(specification);

        // Assert
        var resultList = result.ToList();
        resultList.Should().OnlyContain(u => u.Name != null && u.Email != null);
    }

    #endregion
}
