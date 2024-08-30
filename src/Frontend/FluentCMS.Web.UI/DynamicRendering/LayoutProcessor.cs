using FluentCMS.Providers.TemplateRenderingProviders.Abstractions;
using System.Text.RegularExpressions;

namespace FluentCMS.Web.UI.DynamicRendering;

public interface ILayoutProcessor
{
    List<Segment> ProcessSegments(string htmlContent, Dictionary<string, object> keyValues);
}

public class LayoutProcessor(ITemplateRenderingProvider renderingProvider) : ILayoutProcessor
{
    private Type GetTypeByName(string typeName)
    {
        var type = GetType().Assembly.GetTypes().Where(x => x.Name.Equals(typeName)).FirstOrDefault();

        return type ??
            throw new Exception($"Component type {typeName} not found");
    }

    public List<Segment> ProcessSegments(string htmlContent, Dictionary<string, object> keyValues)
    {
        var content = renderingProvider.Render(htmlContent, keyValues);

        var segments = new List<Segment>();

        var componentsDict = ExtractComponents(content);

        var start_index = 0;
        for (int i = 0; i < componentsDict.Count; i++)
        {
            var currentComponent = componentsDict[i];
            var htmlStartIndex = start_index;
            var htmlEndIndex = int.Parse(currentComponent["start_index"]) - 1;
            var componentStartIndex = int.Parse(currentComponent["start_index"]);
            var componentEndIndex = int.Parse(currentComponent["end_index"]);

            // adding html segment to the list
            // this html content will be rendered as it is
            // before the component
            var htmlMarkup = content[htmlStartIndex..htmlEndIndex];
            segments.Add(new HtmlSegment
            {
                StartIndex = start_index,
                EndIndex = int.Parse(componentsDict[i]["end_index"]),
                Content = htmlMarkup
            });

            // adding component segment to the list
            // this component will be rendered as a component
            // with the attributes provided
            segments.Add(new ComponentSegment
            {
                StartIndex = htmlEndIndex,
                EndIndex = int.Parse(currentComponent["end_index"]),
                Type = GetTypeByName(currentComponent["_type"]),
                Attributes = currentComponent.Where(x => x.Key != "_type" && x.Key != "start_index" && x.Key != "end_index").ToDictionary()
            });

            start_index = int.Parse(currentComponent["end_index"]) + 1;
        }

        // adding the last html segment to the list
        // this html content will be rendered as it is
        // after the last component
        if (start_index < content.Length)
        {
            var htmlMarkup = content[start_index..];
            segments.Add(new HtmlSegment
            {
                StartIndex = start_index,
                EndIndex = content.Length,
                Content = htmlMarkup
            });
        }

        return segments;
    }

    private static List<Dictionary<string, string>> ExtractComponents(string htmlContent)
    {
        var result = new List<Dictionary<string, string>>();

        // Regular expression to find tags with fluentcms attribute
        var tagPattern = new Regex(@"<(?<tag>\w+)(?<attributes>[^>]*?\sfluentcms\s[^>]*?)\/?>", RegexOptions.IgnoreCase);

        // Regular expression to extract attributes
        var attributePattern = new Regex(@"(?<name>\w+)=['""](?<value>[^'""]*)['""]", RegexOptions.IgnoreCase);

        var matches = tagPattern.Matches(htmlContent);

        foreach (Match match in matches)
        {
            var tagName = match.Groups["tag"].Value;
            var attributesString = match.Groups["attributes"].Value;

            var attributes = new Dictionary<string, string>
            {
                { "_type", tagName }
            };

            var attributeMatches = attributePattern.Matches(attributesString);

            foreach (Match attributeMatch in attributeMatches)
            {
                attributes[attributeMatch.Groups["name"].Value] = attributeMatch.Groups["value"].Value;
            }

            //// Adding the "fluentcms" attribute as its presence is what we are checking
            //if (!attributes.ContainsKey("fluentcms"))
            //{
            //    attributes["fluentcms"] = string.Empty;
            //}

            // Add the start and end indices
            attributes["start_index"] = match.Index.ToString();
            attributes["end_index"] = (match.Index + match.Length - 1).ToString();

            result.Add(attributes);
        }

        return result;
    }
}
