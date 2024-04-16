using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.E2eTests.StepDefinitions;

public static partial class ScenarioContextExtensions
{
    public const string ServiceProviderKey = "ServiceProvider";
    public static ServiceProvider GetServiceProvider(this ScenarioContext context)
    {
        return context.Get<ServiceProvider>(ServiceProviderKey);
    }

    public static void FillMetadata(this object obj, Table table, string metadataFieldName = "Metadata")
    {
        var metadataProperty = obj.GetType().GetProperty(metadataFieldName) ?? throw new Exception($"Could not find {metadataFieldName} in {obj.GetType().Name}");
        if (metadataProperty.GetValue(obj) == null)
        {
            metadataProperty.SetValue(obj, new Dictionary<string, object?>());
        }

        var metadataValue = (IDictionary<string, object?>)metadataProperty.GetValue(obj)!;

        var tableMetadataFields = table.Rows.Where(x => x["field"].StartsWith(metadataFieldName, StringComparison.OrdinalIgnoreCase));
        foreach (var tableMetadataField in tableMetadataFields)
        {
            var key = tableMetadataField["field"].Replace(metadataFieldName, "",StringComparison.OrdinalIgnoreCase).TrimStart('.');
            var value = tableMetadataField["value"];
            metadataValue[key] = value;
        }
    }

}
