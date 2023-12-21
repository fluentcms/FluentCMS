using FluentCMS.Repositories;

namespace Microsoft.Extensions.DependencyInjection;

public static class SetupServiceExtensions
{
    public static void ResetDb(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IGlobalSettingsRepository>();
        var taskReset = Task.Run(() => repository.Reset());
        taskReset.Wait();
    }
}
