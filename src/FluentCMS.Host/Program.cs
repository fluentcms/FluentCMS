using FluentCMS.Core.DependencyInjection;
using FluentCMS.Core.Schema;
using FluentCMS.Host;

if (args is [HealthProbe.Argument])
{
    return await HealthProbe.RunAsync(TimeSpan.FromSeconds(5));
}

// A configuration the host cannot start with ends the process with its FCMS message and exit code 1, not a crash.
try
{
    await ReferenceHost.Build(WebApplication.CreateBuilder(args)).RunAsync();
    return 0;
}
catch (Exception exception) when (exception is ReferenceHostException or SchemaValidationException or FluentCmsConfigurationException)
{
    await Console.Error.WriteLineAsync(exception.Message);
    return 1;
}
