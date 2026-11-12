using FluentCMS.Core.Identity.Members;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

public sealed class AppleLogLevelTests
{
    private const string Other = "FluentCMS.Core.Identity.Members.MemberExternalSignIn";

    [Fact]
    public void ADefaultOfTraceDoesNotReachTheAppleCategories()
    {
        using var loggers = Loggers(logging => logging.SetMinimumLevel(LogLevel.Trace));

        Enabled(loggers, Other, LogLevel.Trace).ShouldBeTrue();
        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(loggers, category, LogLevel.Debug));
        AppleLogLevel.Categories.ShouldAllBe(category => Enabled(loggers, category, LogLevel.Information));
    }

    // A rule of one provider outranks a rule of every provider whatever their categories, so the category is held for
    // that provider too.
    [Fact]
    public void ADefaultOfTraceForOneProviderDoesNotReachThem()
    {
        using var loggers = Loggers(logging => logging.AddFilter<Sink>(null, LogLevel.Trace));

        Enabled(loggers, Other, LogLevel.Trace).ShouldBeTrue();
        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(loggers, category, LogLevel.Debug));
    }

    [Fact]
    public void TheHostLowersTheCategoryByNamingIt()
    {
        using var everyProvider = Loggers(logging => logging.AddFilter<Sink>(null, LogLevel.Trace).AddFilter(AppleLogLevel.Category, LogLevel.Trace));
        using var oneProvider = Loggers(logging => logging.SetMinimumLevel(LogLevel.Trace).AddFilter<Sink>(AppleLogLevel.Category, LogLevel.Trace));

        AppleLogLevel.Categories.ShouldAllBe(category => Enabled(everyProvider, category, LogLevel.Trace));
        AppleLogLevel.Categories.ShouldAllBe(category => Enabled(oneProvider, category, LogLevel.Trace));
    }

    // A provider kept above Information, as the event log is on Windows, stays there whatever the category is named.
    [Fact]
    public void ALevelAboveInformationIsKept()
    {
        using var filtered = Loggers(logging => logging.AddFilter<Sink>(level => level >= LogLevel.Warning).AddFilter(AppleLogLevel.Category, LogLevel.Trace));
        using var everyProvider = Loggers(logging => logging.SetMinimumLevel(LogLevel.Warning));
        using var oneProvider = Loggers(logging => logging.AddFilter<Sink>(null, LogLevel.Error).AddFilter(AppleLogLevel.Category, LogLevel.Trace));

        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(filtered, category, LogLevel.Information) && Enabled(filtered, category, LogLevel.Warning));
        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(everyProvider, category, LogLevel.Information) && Enabled(everyProvider, category, LogLevel.Warning));
        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(oneProvider, category, LogLevel.Warning) && Enabled(oneProvider, category, LogLevel.Error));
    }

    [Fact]
    public void ALevelNamedForEveryProviderHoldsForAProviderWithItsOwnDefault()
    {
        using var loggers = Loggers(logging => logging.AddFilter<Sink>(null, LogLevel.Trace).AddFilter(AppleLogLevel.Category, LogLevel.Warning));

        AppleLogLevel.Categories.ShouldAllBe(category => !Enabled(loggers, category, LogLevel.Information));
        AppleLogLevel.Categories.ShouldAllBe(category => Enabled(loggers, category, LogLevel.Warning));
    }

    private static ServiceProvider Loggers(Action<ILoggingBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => configure(logging.AddProvider(new Sink())));
        services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>, AppleLogLevel>();
        return services.BuildServiceProvider();
    }

    private static bool Enabled(ServiceProvider services, string category, LogLevel level) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(category).IsEnabled(level);

    private sealed class Sink : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        public void Dispose()
        {
        }
    }
}
