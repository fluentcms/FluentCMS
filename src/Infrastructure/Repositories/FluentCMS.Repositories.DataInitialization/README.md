# FluentCMS.Repositories.DataInitialization

A lightweight .NET library for conditional database initialization, schema validation, and data seeding in FluentCMS repositories.

## Overview

This library provides a flexible, extensible framework for initializing databases with conditional execution based on environment, configuration, or custom logic. It supports schema validation and creation, as well as priority-based data seeding with idempotent operations.

## Key Features

- **Conditional Execution**: Run initialization based on environment (Dev/Prod), configuration values, or custom predicates
- **Schema Validation**: Ensure required database structures exist before seeding data
- **Priority-Based Seeding**: Execute data seeders in specific order with skip logic for existing data
- **Composite Conditions**: Combine multiple conditions using AND/OR logic
- **Exception Handling**: Configurable exception ignoring for robust initialization
- **Async Support**: Full async/await support with cancellation tokens
- **Lightweight**: Minimal dependencies, .NET 9.0 targeted

## Architecture

### Core Interfaces

#### `IDataSeeder`
Defines contracts for seeding data into databases with priority-based execution and existence checking.

- `Priority`: Execution order (lower numbers first)
- `ShouldSeed()`: Check if data already exists (for idempotency)
- `SeedData()`: Perform the actual data seeding

#### `IDbInitializationCondition`
Controls whether initialization operations should proceed based on runtime factors.

- `Name`: Descriptive identifier for logging
- `ShouldExecute()`: Evaluate condition and return execution decision

#### `ISchemaValidator`
Handles database schema validation and creation.

- `Priority`: Execution order (typically before seeders)
- `ValidateSchema()`: Check if required schema exists
- `CreateSchema()`: Create missing database structures

### Built-in Condition Implementations

#### `EnvironmentCondition`
Controls execution based on hosting environment using custom predicates.

```csharp
// Only seed in Development
new EnvironmentCondition(env, e => e.IsDevelopment())

// Seed in Development or Staging
new EnvironmentCondition(env, e => e.IsDevelopment() || e.IsStaging())
```

#### `ConfigurationCondition`
Evaluates configuration values against expected strings (case-insensitive).

```csharp
// Execute only if config key "EnableSeeding" equals "true"
new ConfigurationCondition(config, "EnableSeeding", "true")
```

#### `CompositeCondition`
Combines multiple conditions with AND/OR logic.

```csharp
// All conditions must be true (AND)
new CompositeCondition(true, condition1, condition2, condition3)

// At least one condition must be true (OR)
new CompositeCondition(false, condition1, condition2, condition3)
```

### Configuration Options

#### `DataSeedingOptions`
```csharp
var options = new DataSeedingOptions
{
    Conditions = new List<IDbInitializationCondition>
    {
        new EnvironmentCondition(hostingEnv, e => e.IsDevelopment()),
        new ConfigurationCondition(config, "Database:EnableSeeding", "true")
    },
    IgnoreExceptions = false  // Fail fast on errors
};
```

#### `SchemaValidationOptions`
```csharp
var schemaOptions = new SchemaValidationOptions
{
    Conditions = new List<IDbInitializationCondition>
    {
        new EnvironmentCondition(hostingEnv, e => !e.IsProduction())
    },
    IgnoreExceptions = true  // Continue on schema validation errors
};
```


## Project Structure

```
FluentCMS.Repositories.DataInitialization/
├── Abstractions/
│   ├── IDataSeeder.cs            # Data seeding contract
│   ├── IDbInitializationCondition.cs  # Conditional execution contract
│   └── ISchemaValidator.cs       # Schema validation contract
├── CompositeCondition.cs         # Logical condition composition
├── ConfigurationCondition.cs     # Configuration-based conditions
├── EnvironmentCondition.cs       # Environment-based conditions
├── DataSeedingOptions.cs         # Seeding configuration
├── SchemaValidationOptions.cs    # Schema validation configuration
└── FluentCMS.Repositories.DataInitialization.csproj
```

## Dependencies

- `Microsoft.Extensions.Hosting.Abstractions` (9.0.9)
- .NET 9.0

## Design Principles

1. **Idempotency**: Operations should be safe to run multiple times
2. **Prioritization**: Schema validation runs before data seeding
3. **Conditionality**: Flexible execution control via composable conditions
4. **Error Resilience**: Configurable exception handling
5. **Async-First**: Full async/await support throughout
6. **Minimal Coupling**: Interfaces keep implementations loosely coupled

## Error Handling

- Use `IgnoreExceptions` in options to continue execution on errors
- Specific exception types for better error context
- Cancellation token support for graceful shutdown
- Detailed error messages in composite condition failures

## Best Practices

1. **Use Gap-Based Priorities**: Space priorities (10, 20, 30) for future insertions
2. **Idempotent Seeders**: Always check `ShouldSeed()` before seeding
3. **Environment-Aware**: Leverage `EnvironmentCondition` for dev/prod differences
4. **Composite Logic**: Use `CompositeCondition` for complex conditional requirements
5. **Configuration-Driven**: Make initialization behavior configurable

## API Stability

This is an internal FluentCMS component. APIs are subject to change based on project needs. External usage should pin specific versions.

## Contributing

When adding new condition types or seeders:
- Implement the appropriate interface
- Provide descriptive `Name` properties
- Handle cancellation tokens properly
- Include comprehensive error handling
- Follow existing naming and documentation conventions

## License

Proprietary - Internal FluentCMS component.
