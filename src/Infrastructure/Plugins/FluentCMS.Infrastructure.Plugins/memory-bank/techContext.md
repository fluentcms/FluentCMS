# Technology Context

## Technologies Used

### Core Framework
- **Target Framework:** .NET 9.0 LTS
- **Runtime:** Cross-platform (.NET Core / .NET 5+)
- **Language:** C# 12.0 with modern features
- **Build System:** MSBuild (via dotnet CLI)

### Key Dependencies

#### Microsoft Extensions
- **Microsoft.Extensions.DependencyInjection** (v9.0.0)
  - Core DI container
  - IServiceCollection, IServiceProvider
  - Scoped, transient, singleton registrations
  
- **Microsoft.Extensions.Configuration** (v9.0.0)
  - Configuration management
  - JSON, environment variables, command line
  - IConfiguration, IConfigurationSection
  
- **Microsoft.Extensions.Logging** (v9.0.0)
  - Structured logging interface
  - ILogger, ILoggerFactory
  - Provider pattern

- **Microsoft.AspNetCore.App** (v9.0.0)
  - ASP.NET Core hosting
  - Middleware pipeline
  - Request/response abstractions
  - Health checks framework

#### Event Bus
  - In-process messaging
  - Request/response and notification patterns  
  - Handler discovery and registration
  - Pipeline behaviors

#### Testing
- **xUnit.net** (latest)
  - Test framework
  - Theories and facts
  - Collection fixtures
  
- **Moq** (latest)
  - Mocking library
  - Interface and abstract class mocking
  - Setup and verification
  
- **Microsoft.Extensions.Logging.Testing** (v9.0.0)
  - Test logging provider
  - Log message capture and assertion

#### Development Tools
- **JetBrains Rider** or **Visual Studio 2022**
  - IDE with .NET 9 support
  - Debugging capabilities
  - Code analysis tools

## Development Setup

### Prerequisites
```bash
# Install .NET 9 SDK
winget install Microsoft.DotNet.SDK.9

# Verify installation
dotnet --version  # Should show 9.x.x
```

### Local Development Environment

#### Solution Structure
```
FluentCMS.sln
├── src/
│   ├── FluentCMS.Infrastructure.Plugins.Abstractions/
│   ├── FluentCMS.Infrastructure.Plugins/
│   └── FluentCMS.Host/  # Example host application
└── tests/
    ├── FluentCMS.Infrastructure.Plugins.Tests/
    └── FluentCMS.Plugins.Example.Tests/
```

#### Project Dependencies
```xml
<!-- FluentCMS.Infrastructure.Plugins.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.App" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="9.0.0" />
  </ItemGroup>

</Project>
```

### Build Process

#### Standard Build
```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Create packages
dotnet pack
```

#### Multi-Targeting
```xml
<!-- Support net8.0 and net9.0 for compatibility -->
<PropertyGroup>
  <TargetFrameworks>net8.0;net9.0</TargetFrameworks>
</PropertyGroup>
```

### Development Workflow

#### IDE Setup
1. Clone repository
2. Open `FluentCMS.sln`
3. Restore NuGet packages
4. Build solution
5. Run tests

#### Command Line Development
```bash
# Development loop
watch run -p FluentCMS.Host
```

#### Dockerfile for Development (Optional)
```dockerfile
# Development container
FROM mcr.microsoft.com/dotnet/sdk:9.0

WORKDIR /app
COPY . .

# Hot reload for development
ENTRYPOINT ["dotnet", "watch", "run", "--project", "FluentCMS.Host"]
```

## Technical Constraints

### .NET Platform Limitations

#### 1. Dependency Injection Container
**Immutable After Build:**
- `services.BuildServiceProvider()` cannot be modified
- No dynamic service registration at runtime
- Plugin services registered before container build

**Solution:** All plugin services registered in `ConfigureServices` phase.

#### 2. Assembly Loading
**Compile-Time Discovery:**
- Cannot dynamically load assemblies at runtime
- All plugins must be known at compile time
- References resolved by MSBuild

**Solution:** Plugin discovery via assembly scanning at startup.

#### 3. Middleware Pipeline
**Fixed Order:**
- Middleware registered in `Configure` phase
- Order determined by `ConfigurePriority`
- No dynamic middleware addition

**Solution:** Priority-based ordering controls pipeline composition.

### Language and Framework Features

#### Modern C# Features Used
- **Records:** Immutable data models for events
- **Init Properties:** Immutable initialization
- **Pattern Matching:** Type checks and casting
- **Async/Await Everywhere:** Non-blocking operations
- **Nullable Reference Types:** Safety from null reference exceptions
- **Top-Level Statements:** Simplified console apps (tests)

#### Required Language Version
```xml
<PropertyGroup>
  <LangVersion>12.0</LangVersion>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
</PropertyGroup>
```

### Performance Constraints

#### Application Startup
- Plugin loading < 2 seconds for 10 plugins
- Memory usage < 100MB baseline overhead
- Thread usage minimized during startup

#### Runtime Performance
- Event publishing < 10ms overhead
- Health checks < 1 second total
- Monitoring overhead < 5%

### Security Constraints

#### Trust Model
- **Trusted Code Only:** All plugins from internal sources
- **Same Security Context:** Host and plugins share process
- **No Sandboxing:** Plugins have full application access

#### Security Boundaries
- **Configuration Isolation:** Plugins can only access their own config sections
- **Dependency Injection Scope:** Controlled interface access
- **Logging:** Structured logging for audit trails

### Compatibility Requirements

#### .NET Version Support
- **Minimum:** .NET 8.0 LTS
- **Target:** .NET 9.0 LTS
- **Support Window:** Current LTS + one previous LTS

#### Operating System
- **Windows:** 10+, Server 2016+
- **Linux:** Most distributions with systemd
- **macOS:** 10.15+ (development only)
- **Containers:** Docker, Kubernetes

## Dependencies and Ecosystem

### Runtime Dependencies

#### Required for Host Applications
```xml
<PackageReference Include="FluentCMS.Infrastructure.Plugins" Version="1.0.0" />
```

#### Required for Plugin Development
```xml
<PackageReference Include="FluentCMS.Infrastructure.Plugins.Abstractions" Version="1.0.0" />
<ProjectReference Include="../FluentCMS.Plugins.Contracts/FluentCMS.Plugins.Contracts.csproj" />
```

### Optional Dependencies

#### Advanced Features
- **Serilog:** Structured logging
- **Polly:** Resilience patterns
- **FluentValidation:** Request validation
- **Entity Framework Core:** Database access
- **Redis:** Caching layer

#### Testing Dependencies
```xml
<PackageReference Include="xUnit" Version="2.8.0" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="Microsoft.Extensions.Logging.Testing" Version="9.0.0" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
```

### External Integration Points

#### Event Bus Integration
**Required Setup:**
```csharp
builder.Services.AddEventBus(config =>
{
    config.RegisterServicesFromAssemblies(
        typeof(Program).Assembly,
        // Add plugin assemblies here
    );
});
```

#### Configuration Integration
**Host Configuration:**
```csharp
builder.Configuration
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .AddCommandLine(args);
```

## Tool Usage Patterns

### Development Tools
- **Git:** Source control with feature branches
- **GitHub:** Pull requests, issues, actions
- **Dependabot:** Automated dependency updates

### Productivity Tools
- **Resharper/Rider:** Code analysis and refactoring
- **Postman:** API testing
- **Seq/ELK Stack:** Log aggregation
- **Application Insights:** Observability

### Build Tools
- **dotnet CLI:** Cross-platform development
- **Docker:** Containerized builds
- **GitHub Actions:** CI/CD pipelines
- **SonarQube:** Code quality analysis

## Deployment and Runtime Considerations

### Production Environment
- **Process Model:** Single process, multiple plugins
- **Memory Management:** Shared heap, plugin isolation through DI
- **Threading:** Async-first, minimal thread creation
- **Health Checks:** Application and plugin-level monitoring

### Scaling Considerations
- **Vertical Scaling:** Single host with multiple plugins
- **Horizontal Scaling:** Multiple hosts with same plugin configuration
- **Load Balancing:** Plugin-aware routing (future consideration)

### Monitoring and Diagnostics
- **Application Insights:** Distributed tracing
- **Prometheus/Grafana:** Metrics collection
- **Structured Logging:** Correlated event tracing
- **Health Endpoints:** System status monitoring
