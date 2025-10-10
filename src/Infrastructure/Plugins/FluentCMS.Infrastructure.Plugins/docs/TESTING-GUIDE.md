# Testing Guide

## Table of Contents

- [Overview](#overview)
- [Testing Strategy](#testing-strategy)
- [Unit Testing](#unit-testing)
- [Integration Testing](#integration-testing)
- [Plugin Testing](#plugin-testing)
- [Mocking Guidelines](#mocking-guidelines)
- [Test Organization](#test-organization)
- [Common Test Scenarios](#common-test-scenarios)
- [Performance Testing](#performance-testing)
- [Best Practices](#best-practices)

## Overview

This guide covers testing strategies and best practices for the FluentCMS Plugin System. It includes examples for unit tests, integration tests, and plugin-specific tests.

### Testing Objectives

1. **Correctness**: Ensure all features work as designed
2. **Reliability**: Verify error handling and edge cases
3. **Performance**: Validate acceptable startup times and resource usage
4. **Maintainability**: Create tests that are easy to understand and update

### Test Projects Structure

```
tests/
├── FluentCMS.Infrastructure.Plugins.Tests/           # Unit tests
├── FluentCMS.Infrastructure.Plugins.IntegrationTests/  # Integration tests
└── FluentCMS.Infrastructure.Plugins.TestHelpers/     # Shared test utilities
```

## Testing Strategy

### Test Pyramid

```
        /\
       /  \      E2E Tests (Few)
      /____\     Integration Tests (Some)
     /      \    Unit Tests (Many)
    /________\
```

**Unit Tests (70%):**
- Test individual components in isolation
- Fast execution
- Mock dependencies

**Integration Tests (25%):**
- Test component interactions
- Test actual plugin loading
- Test with real dependencies where possible

**E2E Tests (5%):**
- Test complete plugin system in host application
- Test real-world scenarios

### Coverage Goals

- **Unit Tests**: > 80% code coverage
- **Integration Tests**: Cover all critical paths
- **E2E Tests**: Cover main user scenarios

## Unit Testing

### Setting Up Unit Tests

**Install Required Packages:**

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  <PackageReference Include="xunit" Version="2.6.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />
  <PackageReference Include="coverlet.collector" Version="6.0.0" />
</ItemGroup>
```

### Testing Plugin Scanner

```csharp
using FluentCMS.Infrastructure.Plugins.Discovery;
using FluentAssertions;
using Xunit;

namespace FluentCMS.Infrastructure.Plugins.Tests.Discovery;

public class PluginScannerTests
{
    [Fact]
    public void ScanForPlugins_WithValidPattern_ReturnsPlugins()
    {
        // Arrange
        var scanner = new PluginScanner();
        var patterns = new[] { "FluentCMS.Plugins.*" };

        // Act
        var plugins = scanner.ScanForPlugins(patterns);

        // Assert
        plugins.Should().NotBeEmpty();
        plugins.Should().AllSatisfy(p => p.Should().BeAssignableTo<IPluginStartup>());
    }

    [Fact]
    public void ScanForPlugins_WithInvalidPattern_ReturnsEmpty()
    {
        // Arrange
        var scanner = new PluginScanner();
        var patterns = new[] { "NonExistent.*" };

        // Act
        var plugins = scanner.ScanForPlugins(patterns);

        // Assert
        plugins.Should().BeEmpty();
    }

    [Fact]
    public void ExtractPluginMetadata_ValidPlugin_ReturnsCorrectMetadata()
    {
        // Arrange
        var scanner = new PluginScanner();
        var plugin = new TestPlugin();

        // Act
        var metadata = scanner.ExtractPluginMetadata(plugin);

        // Assert
        metadata.Name.Should().Be("Test Plugin");
        metadata.Version.Should().NotBeNullOrEmpty();
    }
}

// Test plugin for unit tests
[Plugin]
public class TestPlugin : IPluginStartup
{
    public string Name => "Test Plugin";
    public string Version => "1.0.0";
    public int ConfigureServicesPriority => 100;
    public int ConfigurePriority => 100;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Test implementation
    }

    public void Configure(IApplicationBuilder app, IServiceProvider provider)
    {
        // Test implementation
    }
}
```

### Testing Dependency Graph Builder

```csharp
using FluentCMS.Infrastructure.Plugins.Discovery;
using FluentAssertions;
using Xunit;

namespace FluentCMS.Infrastructure.Plugins.Tests.Discovery;

public class DependencyGraphBuilderTests
{
    [Fact]
    public void BuildGraph_WithNoDependencies_ReturnsCorrectOrder()
    {
        // Arrange
        var builder = new DependencyGraphBuilder();
        var plugins = new List<IPluginStartup>
        {
            new PluginA(),
            new PluginB(),
            new PluginC()
        };

        // Act
        var graph = builder.BuildGraph(plugins);
        var sorted = builder.TopologicalSort(graph);

        // Assert
        sorted.Should().HaveCount(3);
        sorted.Should().ContainInOrder(plugins);
    }

    [Fact]
    public void BuildGraph_WithDependencies_ReturnsCorrectOrder()
    {
        // Arrange
        var builder = new DependencyGraphBuilder();
        var pluginA = new PluginA(); // No dependencies
        var pluginB = new PluginB(); // Depends on A
        var pluginC = new PluginC(); // Depends on B
        var plugins = new List<IPluginStartup> { pluginC, pluginA, pluginB };

        // Act
        var graph = builder.BuildGraph(plugins);
        var sorted = builder.TopologicalSort(graph);

        // Assert
        sorted.Should().HaveCount(3);
        sorted[0].Should().Be(pluginA);
        sorted[1].Should().Be(pluginB);
        sorted[2].Should().Be(pluginC);
    }

    [Fact]
    public void DetectCircularDependencies_WithCircular_ThrowsException()
    {
        // Arrange
        var builder = new DependencyGraphBuilder();
        var plugins = new List<IPluginStartup>
        {
            new CircularPluginA(),
            new CircularPluginB()
        };

        // Act
        var graph = builder.BuildGraph(plugins);
        Action act = () => builder.DetectCircularDependencies(graph);

        // Assert
        act.Should().Throw<PluginCircularDependencyException>()
            .WithMessage("*circular dependency*");
    }
}
```

### Testing Plugin Validator

```csharp
using FluentCMS.Infrastructure.Plugins.Discovery;
using FluentAssertions;
using Xunit;

namespace FluentCMS.Infrastructure.Plugins.Tests.Discovery;

public class PluginValidatorTests
{
    [Fact]
    public void ValidatePlugins_AllValid_ReturnsSuccess()
    {
        // Arrange
        var validator = new PluginValidator();
        var plugins = new List<IPluginStartup>
        {
            new ValidPlugin1(),
            new ValidPlugin2()
        };

        // Act
        var result = validator.ValidatePlugins(plugins);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidatePlugins_DuplicateNames_ReturnsError()
    {
        // Arrange
        var validator = new PluginValidator();
        var plugins = new List<IPluginStartup>
        {
            new DuplicateNamePlugin(),
            new DuplicateNamePlugin()
        };

        // Act
        var result = validator.ValidatePlugins(plugins);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("duplicate"));
    }

    [Fact]
    public void ValidateDependencies_MissingDependency_ReturnsError()
    {
        // Arrange
        var validator = new PluginValidator();
        var plugin = new PluginWithMissingDependency();
        var loadedPlugins = new List<string> { "ExistingPlugin" };

        // Act
        var isValid = validator.ValidateDependencies(plugin, loadedPlugins);

        // Assert
        isValid.Should().BeFalse();
    }
}
```

### Testing Plugin Registry

```csharp
using FluentCMS.Infrastructure.Plugins.Registry;
using FluentAssertions;
using Xunit;

namespace FluentCMS.Infrastructure.Plugins.Tests.Registry;

public class PluginRegistryTests
{
    [Fact]
    public void GetAllPlugins_ReturnsAllRegisteredPlugins()
    {
        // Arrange
        var registry = new PluginRegistry();
        var plugin1 = CreatePluginInfo("Plugin1");
        var plugin2 = CreatePluginInfo("Plugin2");
        registry.Register(plugin1);
        registry.Register(plugin2);

        // Act
        var plugins = registry.GetAllPlugins();

        // Assert
        plugins.Should().HaveCount(2);
        plugins.Should().Contain(p => p.Name == "Plugin1");
        plugins.Should().Contain(p => p.Name == "Plugin2");
    }

    [Fact]
    public void GetPlugin_ExistingPlugin_ReturnsPlugin()
    {
        // Arrange
        var registry = new PluginRegistry();
        var pluginInfo = CreatePluginInfo("TestPlugin");
        registry.Register(pluginInfo);

        // Act
        var result = registry.GetPlugin("TestPlugin");

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("TestPlugin");
    }

    [Fact]
    public void IsPluginLoaded_ExistingPlugin_ReturnsTrue()
    {
        // Arrange
        var registry = new PluginRegistry();
        var pluginInfo = CreatePluginInfo("TestPlugin");
        pluginInfo.Status = PluginStatus.Active;
        registry.Register(pluginInfo);

        // Act
        var isLoaded = registry.IsPluginLoaded("TestPlugin");

        // Assert
        isLoaded.Should().BeTrue();
    }

    [Fact]
    public void UpdatePluginStatus_ValidPlugin_UpdatesStatus()
    {
        // Arrange
        var registry = new PluginRegistry();
        var pluginInfo = CreatePluginInfo("TestPlugin");
        registry.Register(pluginInfo);

        // Act
        registry.UpdatePluginStatus("TestPlugin", PluginStatus.Failed);

        // Assert
        var plugin = registry.GetPlugin("TestPlugin");
        plugin!.Status.Should().Be(PluginStatus.Failed);
    }

    private PluginInfo CreatePluginInfo(string name)
    {
        return new PluginInfo
        {
            Name = name,
            Version = "1.0.0",
            AssemblyName = $"{name}.dll",
            Dependencies = Array.Empty<string>(),
            Status = PluginStatus.Discovered,
            LoadedAt = DateTimeOffset.UtcNow
        };
    }
}
```

## Integration Testing

### Setting Up Integration Tests

Integration tests verify that components work together correctly and test the actual plugin loading process.

```csharp
using FluentCMS.Infrastructure.Plugins;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Infrastructure.Plugins.IntegrationTests;

public class PluginLoadingIntegrationTests : IDisposable
{
    private readonly ServiceCollection _services;
    private readonly IConfiguration _configuration;

    public PluginLoadingIntegrationTests()
    {
        _services = new ServiceCollection();
        
        // Build test configuration
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Plugins:TestPlugin:Setting1"] = "Value1"
            })
            .Build();
    }

    [Fact]
    public async Task LoadPlugins_ValidPlugins_LoadsSuccessfully()
    {
        // Arrange
        var options = new PluginSystemOptions
        {
            ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.Tests.*" },
            IgnoreErrors = false
        };

        _services.AddPluginSystem(opt =>
        {
            opt.ScanAssemblyPatterns = options.ScanAssemblyPatterns;
            opt.IgnoreErrors = options.IgnoreErrors;
        });

        // Act
        var provider = _services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        var plugins = registry.GetAllPlugins();

        // Assert
        plugins.Should().NotBeEmpty();
        plugins.Should().AllSatisfy(p => p.Status.Should().Be(PluginStatus.Active));
    }

    [Fact]
    public async Task LoadPlugins_WithDependencies_LoadsInCorrectOrder()
    {
        // Arrange
        var loadOrder = new List<string>();
        var options = new PluginSystemOptions
        {
            ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.Tests.*" }
        };

        // Act
        _services.AddPluginSystem(opt =>
        {
            opt.ScanAssemblyPatterns = options.ScanAssemblyPatterns;
        });

        var provider = _services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IPluginRegistry>();
        var plugins = registry.GetAllPlugins();

        // Assert
        // Verify dependencies loaded before dependents
        var pluginA = plugins.FirstOrDefault(p => p.Name == "PluginA");
        var pluginB = plugins.FirstOrDefault(p => p.Name == "PluginB");
        
        if (pluginA != null && pluginB != null && pluginB.Dependencies.Contains("PluginA"))
        {
            plugins.IndexOf(pluginA).Should().BeLessThan(plugins.IndexOf(pluginB));
        }
    }

    [Fact]
    public async Task LoadPlugins_LifecycleEvents_ArePublished()
    {
        // Arrange
        var eventsReceived = new List<string>();
        var options = new PluginSystemOptions
        {
            ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.Tests.*" }
        };

        _services.AddSingleton<IEventSubscriber<PluginLoadingEvent>>(
            new TestEventSubscriber<PluginLoadingEvent>(e => eventsReceived.Add("Loading")));
        _services.AddSingleton<IEventSubscriber<ApplicationStartedEvent>>(
            new TestEventSubscriber<ApplicationStartedEvent>(e => eventsReceived.Add("Started")));

        // Act
        _services.AddPluginSystem(opt =>
        {
            opt.ScanAssemblyPatterns = options.ScanAssemblyPatterns;
        });

        var provider = _services.BuildServiceProvider();
        await Task.Delay(100); // Give events time to process

        // Assert
        eventsReceived.Should().Contain("Loading");
        eventsReceived.Should().Contain("Started");
    }

    public void Dispose()
    {
        // Cleanup
    }
}

// Test event subscriber helper
public class TestEventSubscriber<TEvent> : IEventSubscriber<TEvent> 
    where TEvent : class, IEvent
{
    private readonly Action<TEvent> _handler;

    public TestEventSubscriber(Action<TEvent> handler)
    {
        _handler = handler;
    }

    public Task Handle(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        _handler(domainEvent);
        return Task.CompletedTask;
    }
}
```

## Plugin Testing

### Testing Your Plugin

When developing a plugin, create dedicated tests:

```csharp
using FluentCMS.Plugins.TextWidget;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluentCMS.Plugins.TextWidget.Tests;

public class TextWidgetPluginTests
{
    [Fact]
    public void ConfigureServices_RegistersServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var plugin = new TextWidgetStartup();

        // Act
        plugin.ConfigureServices(services, configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var service = provider.GetService<ITextWidgetService>();
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task TextWidgetService_RenderWidget_ReturnsExpectedOutput()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Plugins:TextWidget:MaxLength"] = "1000",
                ["Plugins:TextWidget:AllowHtml"] = "false"
            })
            .Build();

        services.AddLogging();
        services.Configure<TextWidgetSettings>(
            configuration.GetSection("Plugins:TextWidget"));
        services.AddScoped<ITextWidgetService, TextWidgetService>();

        var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ITextWidgetService>();

        // Act
        var result = await service.RenderWidget("Test Content");

        // Assert
        result.Should().Contain("Test Content");
        result.Should().Contain("text-widget");
    }
}
```

## Mocking Guidelines

### Mocking Dependencies

Use Moq for mocking dependencies:

```csharp
using Moq;
using FluentCMS.Infrastructure.EventBus.Abstractions;

public class ServiceWithDependenciesTests
{
    [Fact]
    public async Task Method_WithMockedDependency_WorksCorrectly()
    {
        // Arrange
        var mockEventPublisher = new Mock<IEventPublisher>();
        var mockLogger = new Mock<ILogger<MyService>>();
        
        mockEventPublisher
            .Setup(x => x.Publish(It.IsAny<SomeEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new MyService(mockEventPublisher.Object, mockLogger.Object);

        // Act
        await service.DoSomething();

        // Assert
        mockEventPublisher.Verify(
            x => x.Publish(It.IsAny<SomeEvent>(), It.IsAny<CancellationToken>()), 
            Times.Once);
    }
}
```

### Mocking Configuration

```csharp
using Microsoft.Extensions.Options;
using Moq;

public class ServiceWithConfigurationTests
{
    [Fact]
    public void Method_WithConfiguration_UsesCorrectSettings()
    {
        // Arrange
        var settings = new MyPluginSettings
        {
            MaxLength = 500,
            AllowHtml = true
        };

        var mockOptions = new Mock<IOptions<MyPluginSettings>>();
        mockOptions.Setup(x => x.Value).Returns(settings);

        var service = new MyService(mockOptions.Object);

        // Act & Assert
        service.MaxLength.Should().Be(500);
    }
}
```

## Test Organization

### Folder Structure

```
FluentCMS.Infrastructure.Plugins.Tests/
├── Discovery/
│   ├── PluginScannerTests.cs
│   ├── DependencyGraphBuilderTests.cs
│   └── PluginValidatorTests.cs
├── Loading/
│   ├── PluginLoaderTests.cs
│   └── ServiceRegistrarTests.cs
├── Registry/
│   └── PluginRegistryTests.cs
├── Lifecycle/
│   ├── LifecycleEventPublisherTests.cs
│   └── PluginStateManagerTests.cs
└── TestHelpers/
    ├── TestPlugin.cs
    ├── TestPluginWithDependency.cs
    └── MockFactory.cs
```

### Naming Conventions

```csharp
// Class: [TestedClass]Tests
public class PluginScannerTests { }

// Method: [MethodName]_[Scenario]_[ExpectedBehavior]
[Fact]
public void ScanForPlugins_WithValidPattern_ReturnsPlugins() { }

[Fact]
public void ScanForPlugins_WithInvalidPattern_ReturnsEmpty() { }
```

## Common Test Scenarios

### Testing Error Handling

```csharp
[Fact]
public void Method_WithInvalidInput_ThrowsArgumentException()
{
    // Arrange
    var service = new MyService();

    // Act
    Action act = () => service.Process(null);

    // Assert
    act.Should().Throw<ArgumentNullException>()
        .WithParameterName("input");
}
```

### Testing Async Methods

```csharp
[Fact]
public async Task AsyncMethod_ValidInput_ReturnsExpectedResult()
{
    // Arrange
    var service = new MyService();

    // Act
    var result = await service.ProcessAsync("input");

    // Assert
    result.Should().NotBeNull();
}
```

### Testing with Cancellation Tokens

```csharp
[Fact]
public async Task Method_CancellationRequested_ThrowsOperationCanceledException()
{
    // Arrange
    var service = new MyService();
    var cts = new CancellationTokenSource();
    cts.Cancel();

    // Act
    Func<Task> act = async () => await service.LongRunningOperation(cts.Token);

    // Assert
    await act.Should().ThrowAsync<OperationCanceledException>();
}
```

## Performance Testing

### Startup Time Testing

```csharp
using System.Diagnostics;

[Fact]
public void LoadPlugins_WithTenPlugins_CompletesWithinTwoSeconds()
{
    // Arrange
    var stopwatch = Stopwatch.StartNew();
    var options = new PluginSystemOptions
    {
        ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.*" }
    };

    // Act
    var loader = new PluginLoader();
    var plugins = loader.LoadPlugins(options);

    stopwatch.Stop();

    // Assert
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000);
}
```

### Memory Usage Testing

```csharp
[Fact]
public void LoadPlugins_MemoryUsage_WithinAcceptableRange()
{
    // Arrange
    var beforeMemory = GC.GetTotalMemory(true);
    var options = new PluginSystemOptions
    {
        ScanAssemblyPatterns = new[] { "FluentCMS.Plugins.*" }
    };

    // Act
    var loader = new PluginLoader();
    var plugins = loader.LoadPlugins(options);
    var afterMemory = GC.GetTotalMemory(false);

    // Assert
    var memoryUsedMB = (afterMemory - beforeMemory) / 1024.0 / 1024.0;
    memoryUsedMB.Should().BeLessThan(100); // Less than 100 MB
}
```

## Best Practices

### 1. Arrange-Act-Assert Pattern

Always structure tests using AAA pattern:

```csharp
[Fact]
public void Method_Scenario_ExpectedBehavior()
{
    // Arrange
    var service = new MyService();
    var input = "test";

    // Act
    var result = service.Process(input);

    // Assert
    result.Should().Be("expected");
}
```

### 2. One Assert Per Test

Focus each test on one specific behavior:

```csharp
// Good
[Fact]
public void Process_ValidInput_ReturnsNonNull()
{
    var result = service.Process("input");
    result.Should().NotBeNull();
}

[Fact]
public void Process_ValidInput_ReturnsCorrectType()
{
    var result = service.Process("input");
    result.Should().BeOfType<ExpectedType>();
}

// Avoid
[Fact]
public void Process_ValidInput_MultipleAsserts()
{
    var result = service.Process("input");
    result.Should().NotBeNull();
    result.Should().BeOfType<ExpectedType>();
    result.Value.Should().Be(10);
}
```

### 3. Use Test Helpers

Create reusable test helpers:

```csharp
public static class TestHelpers
{
    public static IServiceProvider CreateServiceProvider(
        Action<IServiceCollection> configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static PluginInfo CreateTestPluginInfo(string name)
    {
        return new PluginInfo
        {
            Name = name,
            Version = "1.0.0",
            Status = PluginStatus.Active,
            LoadedAt = DateTimeOffset.UtcNow
        };
    }
}
```

### 4. Clean Up Resources

Implement IDisposable for integration tests:

```csharp
public class IntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public IntegrationTests()
    {
        var services = new ServiceCollection();
        // Setup
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
```

### 5. Use Theory for Parameterized Tests

```csharp
[Theory]
[InlineData("input1", "expected1")]
[InlineData("input2", "expected2")]
[InlineData("input3", "expected3")]
public void Process_VariousInputs_ReturnsExpectedOutput(string input, string expected)
{
    var result = service.Process(input);
    result.Should().Be(expected);
}
```

---

**Next**: [Examples](./EXAMPLES.md)
