using System.Reflection;
using System.Text.Json;

namespace FluentCMS.E2eTests.StepDefinitions;

public partial class StepDefinitions
{
    [Then("I should see the field")]
    public async Task ThenIShouldSeeTheFieldAsync()
    {
        var client = context.Get<ContentTypeClient>();
        var fieldRequest = context.Get<ContentTypeFieldSetRequest>();

        // fetch all content types
        var result = await client.GetAllAsync();

        // find the content type
        var contentType = context.Get<ContentTypeDetailResponseIApiResult>();

        var updatedContentType = result.Data!.Single(x => x.Id == contentType.Data.Id);

        // find the field
        var fieldResponse = updatedContentType.Fields!.SingleOrDefault(x => x.Slug == contentType.Data.Fields!.First().Slug);

        // assert
        fieldResponse.ShouldNotBeNull();

        var fieldProperties = fieldResponse.GetType().GetProperties().Select(x => x.Name);
        foreach (var fieldProperty in fieldProperties)
        {
            var requestProperty = fieldRequest.GetType().GetProperty(fieldProperty);
            var responseProperty = fieldResponse.GetType().GetProperty(fieldProperty);
            var requestValue = requestProperty!.GetValue(fieldRequest)!;
            var responseValue = responseProperty!.GetValue(fieldResponse)!;
            if ((requestValue is IDictionary<string, object?> requestDict) && (responseValue is IDictionary<string, object?> responseDict))
            {
                foreach (var requestDictKey in requestDict.Keys)
                {
                    if (responseDict[requestDictKey] is JsonElement jsonValue)
                    {
                        
                        var convertedValue = mapValue(jsonValue) ;
                        requestDict[requestDictKey].ShouldBeEquivalentTo(convertedValue);
                    }
                    else
                    {
                        requestDict[requestDictKey].ShouldBeEquivalentTo(responseDict[requestDictKey]);
                    }
                }
                return;
            }
            requestValue.ShouldBeEquivalentTo(responseValue);
        }
    }

    private object? mapValue(object value)
    {
        var jsonElement = (JsonElement)value;
        return jsonElement.ValueKind switch
        {
            JsonValueKind.Undefined => null,
            JsonValueKind.Null => null,
            JsonValueKind.Object => jsonElement.EnumerateObject()
                .Select(x => (x.Name, Value: mapValue(x.Value)))
                .ToDictionary(x => x.Name, x => x.Value),
            JsonValueKind.Array => jsonElement.EnumerateArray().Select(x => mapValue(x)).ToArray(),
            JsonValueKind.String => jsonElement.GetString(),
            JsonValueKind.Number => jsonElement
                .GetDecimal(), // todo: find a better way for this as most of our values don't need to be Decimal
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }
}
