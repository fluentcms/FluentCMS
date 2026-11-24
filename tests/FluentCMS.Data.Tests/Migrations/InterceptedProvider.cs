using System.Reflection;
using FluentCMS.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Data.Tests.Migrations;

// The provider of the run with what a member returns passed through a function of the test, for a runner of the test's own.
#pragma warning disable CA1852 // DispatchProxy derives from it.
internal class InterceptedProvider : DispatchProxy
#pragma warning restore CA1852
{
    private IDataProvider _inner = null!;
    private Func<string, object?, object?> _returns = null!;

    public static MigrationRunner Runner(IServiceProvider services, Func<string, object?, object?> returns)
    {
        var proxy = Create<IDataProvider, InterceptedProvider>();
        var intercepted = (InterceptedProvider)(object)proxy;
        intercepted._inner = services.GetRequiredService<IDataProvider>();
        intercepted._returns = returns;
        return ActivatorUtilities.CreateInstance<MigrationRunner>(services, proxy);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        _returns(targetMethod!.Name, targetMethod.Invoke(_inner, BindingFlags.DoNotWrapExceptions, null, args, null));
}
