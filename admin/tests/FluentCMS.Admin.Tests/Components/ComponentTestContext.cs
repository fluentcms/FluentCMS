using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Tests.Components;

public abstract class ComponentTestContext : BunitContext
{
    protected ComponentTestContext()
    {
        Services.AddCmsComponents();
        // The library's scripts do not run under bUnit; what reaches the browser is checked in the markup and the JS calls.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
