using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsAdminPlugin(typeof(ReviewsAdminPlugin))]

namespace FluentCMS.Admin.Plugins.Sample;

[FluentCmsAdminPlugin("reviews", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class ReviewsAdminPlugin : IAdminPlugin
{
    private static int configured;

    // How often Configure ran in the process, for the test that it runs once per load.
    public static int Configured => configured;

    public void Configure(IAdminPluginBuilder admin)
    {
        Interlocked.Increment(ref configured);
        admin.Services.AddScoped<ReviewsApi>();
        admin.Filters.Add("rating", FilterOperand.WholeNumber, FilterOperator.Equal, FilterOperator.GreaterThan);
    }
}

public sealed class ReviewsApi
{
    public int Approved { get; set; }
}
