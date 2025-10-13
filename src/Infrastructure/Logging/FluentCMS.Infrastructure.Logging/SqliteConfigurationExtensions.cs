using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace FluentCMS.Infrastructure.Logging;

public static class SqliteConfigurationExtensions
{
    public static ILoggerFactory InitLogFactory(this IHostBuilder host)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/myapp-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        host.UseSerilog();

        return new SerilogLoggerFactory(Log.Logger);
    }
}
