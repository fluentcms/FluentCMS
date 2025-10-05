# FluentCMS.Repositories.DataInitialization.EntityFramework

A .NET library providing Entity Framework Core implementations for database initialization functionality within the FluentCMS ecosystem. This package contains abstract base classes for data seeding and schema validation that can be used to initialize and validate Entity Framework databases.

## Overview

This library provides two main abstract classes that serve as foundation for database initialization tasks:

- **`BaseDataSeeder<TDbContext>`** - Handles data seeding operations
- **`BaseSchemaValidator<TDbContext>`** - Manages database schema creation and validation

## Features

### BaseDataSeeder<TDbContext>

An abstract base class for implementing data seeders that populate databases with initial data.

**Key Methods:**
- `ShouldSeed()` - Determines if data seeding should occur by checking if tables already contain data
- `SeedData()` - Abstract method that must be implemented by derived classes to perform the actual data seeding

**Key Properties:**
- `Priority` - Abstract property that defines the execution order of multiple seeders

**Default Behavior:**
- Checks for existing data using `DbContext.AnyTablesHaveData()` extension method
- Only seeds when no data is present in the database
- Includes comprehensive logging

### BaseSchemaValidator<TDbContext>

An abstract base class for implementing database schema validators and creators.

**Key Methods:**
- `CreateSchema()` - Creates the database schema by generating and executing the CREATE script
- `ValidateSchema()` - Validates that the database schema exists by checking for table presence

**Key Properties:**
- `Priority` - Abstract property that defines the execution order of multiple validators

**Default Behavior:**
- Uses Entity Framework's `Database.GenerateCreateScript()` for schema creation
- Validates schema existence via `DbContext.AnyTablesExist()` extension method
- Includes comprehensive logging

## Usage

### Implementing a Data Seeder

```csharp
using FluentCMS.Repositories.DataInitialization.EntityFramework;
using Microsoft.Extensions.Logging;

public class MyDataSeeder(AppDbContext dbContext, ILogger<MyDataSeeder> logger)
    : BaseDataSeeder<AppDbContext>(dbContext, logger)
{
    public override int Priority => 1;

    public override async Task SeedData(CancellationToken cancellationToken = default)
    {
        // Add your seeding logic here
        await DbContext.Users.AddAsync(new User { Name = "Admin" }, cancellationToken);
        await DbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### Implementing a Schema Validator

```csharp
using FluentCMS.Repositories.DataInitialization.EntityFramework;
using Microsoft.Extensions.Logging;

public class MySchemaValidator(AppDbContext dbContext, ILogger<MySchemaValidator> logger)
    : BaseSchemaValidator<AppDbContext>(dbContext, logger)
{
    public override int Priority => 0; // Run before seeders
}
```

## Dependencies

This package depends on:
- [.NET 9.0](https://dotnet.microsoft.com/)
- `FluentCMS.Repositories.DataInitialization.Abstractions` - Contains the interface definitions
- `FluentCMS.Repositories.EntityFramework` - Provides Entity Framework extensions
- `Microsoft.EntityFrameworkCore.Relational` v9.0.9 - Core Entity Framework functionality

## Requirements

- [.NET 9.0](https://dotnet.microsoft.com/download/dotnet/9.0) or later
- An Entity Framework Core DbContext implementation

## Installation

This package is part of the FluentCMS monorepo and should be built as part of the overall project. For standalone usage:

```bash
dotnet add package FluentCMS.Repositories.DataInitialization.EntityFramework
```

## Architecture

This package follows the abstractions provided by `FluentCMS.Repositories.DataInitialization.Abstractions`, implementing the concrete Entity Framework versions of:

- `IDataSeeder` - Interface for data seeding operations
- `ISchemaValidator` - Interface for schema validation operations

The abstract base classes provide common functionality while allowing for customization through inheritance.

## Logging

Both base classes include comprehensive logging using Microsoft.Extensions.Logging:

- **BaseDataSeeder**: Logs data existence checks and seeding operations
- **BaseSchemaValidator**: Logs schema creation and validation operations

Log levels used:
- `LogInformation` for normal operations
- Custom contexts include the DbContext type name for clear identification

## Cancellation Support

All asynchronous methods accept `CancellationToken` parameters (with default values) for proper async cancellation support.

## Priority System

Both classes implement a priority system (via the abstract `Priority` property) to ensure proper execution order when multiple seeders or validators are registered. Lower priority numbers execute first.

## Thread Safety

These classes are designed to work with dependency injection containers and should be registered as scoped or transient services. The base classes themselves don't maintain state but rely on the injected DbContext.

## Contributing

As part of the FluentCMS project, contributions should follow the project's coding standards and conventions specified in the root repository.

## License

This package is part of the FluentCMS project. Please refer to the main repository for licensing information.
