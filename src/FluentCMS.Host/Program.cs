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
catch (Exception exception) when (IsConfiguration(exception))
{
    IReadOnlyList<Exception> errors = exception is AggregateException aggregate ? aggregate.InnerExceptions : [exception];
    foreach (var error in errors)
    {
        await Console.Error.WriteLineAsync(error.Message);
    }

    return 1;
}

// Production refuses several settings at once as an AggregateException of them.
static bool IsConfiguration(Exception exception) => exception switch
{
    ReferenceHostException or SchemaValidationException or FluentCmsConfigurationException => true,
    AggregateException aggregate => aggregate.InnerExceptions.All(IsConfiguration),
    _ => false,
};
