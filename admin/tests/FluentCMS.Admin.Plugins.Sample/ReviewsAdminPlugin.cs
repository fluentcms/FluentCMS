using FluentCMS.Admin.Lists;
using FluentCMS.Admin.Plugins;
using FluentCMS.Admin.Plugins.Sample;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsAdminPlugin(typeof(ReviewsAdminPlugin))]

namespace FluentCMS.Admin.Plugins.Sample;

[FluentCmsAdminPlugin("reviews", Version = "1.0.0", AdminRange = "[1.0.0,2.0.0)")]
public sealed class ReviewsAdminPlugin : IAdminPlugin
{
    public void Configure(IAdminPluginBuilder admin)
    {
        admin.Services.AddScoped<ReviewsApi>();
        admin.Filters.Add("rating", FilterOperand.WholeNumber, FilterOperator.Equal, FilterOperator.GreaterThan);
    }
}

public sealed class ReviewsApi
{
    public int Approved { get; set; }
}
