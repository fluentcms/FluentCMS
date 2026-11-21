using System.Runtime.CompilerServices;
using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsAdminPlugin(typeof(ReviewsAdminPlugin))]

namespace FluentCMS.Admin.Plugins.Sample;

[FluentCmsAdminPlugin("reviews", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class ReviewsAdminPlugin : IAdminPlugin
{
    // Counted per admin, by the filter registry of its services: the tests of other classes, which run at the same time,
    // configure the half in admins of their own.
    private static readonly ConditionalWeakTable<ListFilterRegistry, StrongBox<int>> configured = new();

    // How often Configure ran for the admin whose filters these are, for the test that it runs once per load.
    public static int ConfiguredFor(ListFilterRegistry filters) => configured.TryGetValue(filters, out var count) ? Volatile.Read(ref count.Value) : 0;

    public void Configure(IAdminPluginBuilder admin)
    {
        Interlocked.Increment(ref configured.GetValue(admin.Filters, _ => new StrongBox<int>()).Value);
        admin.Services.AddScoped<ReviewsApi>();
        admin.Filters.Add("rating", FilterOperand.WholeNumber, FilterOperator.Equal, FilterOperator.GreaterThan);
    }
}

public sealed class ReviewsApi
{
    public int Approved { get; set; }
}
