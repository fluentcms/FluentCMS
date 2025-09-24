# FluentCMS Core Provider System

A comprehensive, pluggable provider system for FluentCMS that enables modular architecture through discoverable, configurable, and swappable service implementations.

## Overview

The FluentCMS Core Provider System is designed to support multiple implementations of the same functionality while maintaining clean separation of concerns, thread safety, and high performance. This system allows you to plug in different providers for services like email, file storage, caching, logging, and more, with only one provider active per functional area at any given time.

## Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
├──────────────────────────────────────────────────────────────┤
│  IEmailProvider  │ IStorageProvider │ ICacheProvider │ ...   │
├──────────────────────────────────────────────────────────────┤
│                 FluentCMS.Providers                          │
│  ┌─────────────────┐ ┌─────────────────┐ ┌──────────────────┐│
│  │ ProviderManager │ │ Provider        │ │ Provider         ││
│  │                 │ │ Discovery       │ │ Resolution       ││
│  └─────────────────┘ └─────────────────┘ └──────────────────┘│
├──────────────────────────────────────────────────────────────┤
│            FluentCMS.Providers.Abstractions                  │
│    IProvider │ IProviderModule │ ProviderModuleBase          │
├──────────────────────────────────────────────────────────────┤
│                 Repository Layer                             │
│  Configuration    │           Entity Framework               │
│  Repository       │           Repository                     │
└──────────────────────────────────────────────────────────────┘
```

## Key Features

- **🔌 Pluggable Architecture**: Easy provider swapping without code changes
- **🔍 Auto-Discovery**: Automatic provider module detection via assembly scanning
- **⚡ High Performance**: Optimized caching, lazy loading, and thread-safe operations
- **🛡️ Thread-Safe**: Concurrent access with proper synchronization primitives
- **📊 Configuration Flexible**: Support for both database and configuration file-based management
- **🎯 Dependency Injection**: Full ASP.NET Core DI container integration
- **📈 Performance Monitoring**: Built-in caching and performance optimizations
- **🔒 Data Integrity**: Database constraints ensuring single active provider per area

## Projects

| Project | Description | Documentation |
|---------|-------------|---------------|
| **FluentCMS.Providers.Abstractions** | Core interfaces and base classes for provider implementations | [📖 View Documentation](FluentCMS.Providers.Abstractions/README.md) |
| **FluentCMS.Providers** | Main provider system implementation with discovery, caching, and management | [📖 View Documentation](FluentCMS.Providers/README.md) |
| **FluentCMS.Providers.Repositories.EntityFramework** | Entity Framework repository implementation for database-backed provider storage | [📖 View Documentation](FluentCMS.Providers.Repositories.EntityFramework/README.md) |

## Quick Start

### Installation

```bash
# Core provider system
dotnet add package FluentCMS.Providers

# For creating custom providers
dotnet add package FluentCMS.Providers.Abstractions

# For database storage
dotnet add package FluentCMS.Providers.Repositories.EntityFramework
```

### Basic Setup

```csharp
// Program.cs
builder.Services.AddProviders(options =>
{
    options.AssemblyPrefixesToScan.Add("FluentCMS");
    options.AssemblyPrefixesToScan.Add("MyCompany");
});

// Configuration-based storage
builder.Services.AddProviders().UseConfiguration();

// OR database storage  
builder.Services.AddProviders().UseEntityFramework();
```

### Usage

```csharp
public class EmailService
{
    private readonly IEmailProvider _emailProvider;
    
    public EmailService(IEmailProvider emailProvider)
    {
        _emailProvider = emailProvider;
    }
    
    public async Task SendWelcomeEmail(string email)
    {
        await _emailProvider.SendEmailAsync(email, "Welcome!", "Thank you for joining!");
    }
}
```

## Documentation

For detailed documentation, examples, and advanced usage scenarios, please refer to the individual project documentation:

### 📖 **Core Documentation**
- **[FluentCMS.Providers](FluentCMS.Providers/README.md)** - Complete setup guide, configuration options, usage examples, and performance optimization
- **[FluentCMS.Providers.Abstractions](FluentCMS.Providers.Abstractions/README.md)** - Creating custom providers, interface design patterns, and best practices
- **[FluentCMS.Providers.Repositories.EntityFramework](FluentCMS.Providers.Repositories.EntityFramework/README.md)** - Database setup, migrations, advanced queries, and multi-tenant support

### 🚀 **Key Topics Covered**
- **Getting Started**: Installation, basic setup, and first provider
- **Provider Development**: Creating custom providers with step-by-step guides
- **Configuration**: Multiple storage options (configuration files, database)
- **Performance**: Caching strategies, optimization tips, and benchmarks
- **Advanced Scenarios**: Provider switching, health monitoring, multi-environment setup
- **Database Integration**: Schema design, migrations, and advanced queries
- **Troubleshooting**: Common issues, debugging tips, and solutions

## Performance & Reliability

This system has been optimized for production use with:

- **Thread-safe operations** with proper synchronization
- **Memory-efficient** assembly loading and caching
- **Database integrity** constraints and transaction management
- **High-performance** provider resolution (O(1) cached lookups)
- **Comprehensive error handling** with meaningful diagnostics

For detailed performance characteristics and optimization guides, see the [FluentCMS.Providers documentation](FluentCMS.Providers/README.md#performance-considerations).

## Contributing

1. Fork the repository
2. Create a feature branch
3. Add comprehensive tests
4. Update relevant documentation
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- 📚 **Documentation**: Individual project README files contain comprehensive guides
- 🐛 **Issues**: Report bugs or request features on GitHub
- 💬 **Discussions**: Join the community discussions
- 📧 **Contact**: Reach out to the maintainers

---

*Built with ❤️ for the FluentCMS ecosystem*
