using FluentCMS.Repositories.Tests.Integration.TestEntities;

namespace FluentCMS.Repositories.Tests.Integration.Helpers;

public static class TestDataBuilder
{
    #region TestUser Builders

    public static TestUser CreateTestUser(
        string name = "John Doe",
        string email = "john.doe@example.com",
        int age = 30,
        Guid? id = null)
    {
        return new TestUser
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Email = email,
            Age = age
        };
    }

    public static TestUser CreateTestUserWithEmptyId(
        string name = "John Doe",
        string email = "john.doe@example.com",
        int age = 30)
    {
        return new TestUser
        {
            Id = Guid.Empty,
            Name = name,
            Email = email,
            Age = age
        };
    }

    public static List<TestUser> CreateTestUsers(int count)
    {
        var users = new List<TestUser>();
        for (int i = 1; i <= count; i++)
        {
            users.Add(CreateTestUser(
                name: $"User {i}",
                email: $"user{i}@example.com",
                age: 20 + (i % 50) // Ages between 20-69
            ));
        }
        return users;
    }

    public static List<TestUser> CreateTestUsersWithEmptyIds(int count)
    {
        var users = new List<TestUser>();
        for (int i = 1; i <= count; i++)
        {
            users.Add(CreateTestUserWithEmptyId(
                name: $"User {i}",
                email: $"user{i}@example.com",
                age: 20 + (i % 50)
            ));
        }
        return users;
    }

    #endregion

    #region TestProduct Builders

    public static TestProduct CreateTestProduct(
        string name = "Test Product",
        decimal price = 99.99m,
        int categoryId = 1,
        bool isActive = true,
        Guid? id = null)
    {
        return new TestProduct
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Price = price,
            CategoryId = categoryId,
            IsActive = isActive
        };
    }

    public static TestProduct CreateTestProductWithEmptyId(
        string name = "Test Product",
        decimal price = 99.99m,
        int categoryId = 1,
        bool isActive = true)
    {
        return new TestProduct
        {
            Id = Guid.Empty,
            Name = name,
            Price = price,
            CategoryId = categoryId,
            IsActive = isActive
        };
    }

    public static List<TestProduct> CreateTestProducts(int count)
    {
        var products = new List<TestProduct>();
        for (int i = 1; i <= count; i++)
        {
            products.Add(CreateTestProduct(
                name: $"Product {i}",
                price: Math.Round((decimal)(10.0 + (i * 5.5)), 2), // Prices from 15.50, 21.00, etc.
                categoryId: (i % 5) + 1, // Categories 1-5
                isActive: i % 3 != 0 // 2/3 active, 1/3 inactive
            ));
        }
        return products;
    }

    public static List<TestProduct> CreateTestProductsWithEmptyIds(int count)
    {
        var products = new List<TestProduct>();
        for (int i = 1; i <= count; i++)
        {
            products.Add(CreateTestProductWithEmptyId(
                name: $"Product {i}",
                price: Math.Round((decimal)(10.0 + (i * 5.5)), 2),
                categoryId: (i % 5) + 1,
                isActive: i % 3 != 0
            ));
        }
        return products;
    }

    #endregion

    #region TestCategory Builders

    public static TestCategory CreateTestCategory(
        string name = "Test Category",
        string description = "Test Category Description",
        Guid? id = null)
    {
        return new TestCategory
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            Description = description
        };
    }

    public static TestCategory CreateTestCategoryWithEmptyId(
        string name = "Test Category",
        string description = "Test Category Description")
    {
        return new TestCategory
        {
            Id = Guid.Empty,
            Name = name,
            Description = description
        };
    }

    public static List<TestCategory> CreateTestCategories(int count)
    {
        var categories = new List<TestCategory>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(CreateTestCategory(
                name: $"Category {i}",
                description: $"Description for Category {i}"
            ));
        }
        return categories;
    }

    public static List<TestCategory> CreateTestCategoriesWithEmptyIds(int count)
    {
        var categories = new List<TestCategory>();
        for (int i = 1; i <= count; i++)
        {
            categories.Add(CreateTestCategoryWithEmptyId(
                name: $"Category {i}",
                description: $"Description for Category {i}"
            ));
        }
        return categories;
    }

    #endregion

    #region Special Data Builders

    public static List<TestUser> CreateUsersForSorting()
    {
        return new List<TestUser>
        {
            CreateTestUser("Alice Smith", "alice@example.com", 25),
            CreateTestUser("Bob Johnson", "bob@example.com", 35),
            CreateTestUser("Charlie Brown", "charlie@example.com", 30),
            CreateTestUser("Diana Prince", "diana@example.com", 28),
            CreateTestUser("Eva Green", "eva@example.com", 22)
        };
    }

    public static List<TestProduct> CreateProductsForAggregation()
    {
        return new List<TestProduct>
        {
            CreateTestProduct("Product A", 10.50m, 1, true),
            CreateTestProduct("Product B", 25.00m, 1, true),
            CreateTestProduct("Product C", 15.75m, 2, true),
            CreateTestProduct("Product D", 30.00m, 2, false),
            CreateTestProduct("Product E", 5.25m, 3, true)
        };
    }

    public static List<TestUser> CreateUsersForFiltering()
    {
        return new List<TestUser>
        {
            CreateTestUser("John Doe", "john@example.com", 30),
            CreateTestUser("Jane Smith", "jane@example.com", 25),
            CreateTestUser("Bob Wilson", "bob@example.com", 35),
            CreateTestUser("Alice Brown", "alice@example.com", 28),
            CreateTestUser("Charlie Davis", "charlie@example.com", 42)
        };
    }

    #endregion
}
