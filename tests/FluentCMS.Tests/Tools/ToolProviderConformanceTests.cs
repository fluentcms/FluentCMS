using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Builtin.Tools;
using FluentCMS.Core.Tools;
using FluentCMS.Tests.Schema;

namespace FluentCMS.Tests.Tools;

// The contract every tool and resource provider meets, built in or not (EXT-N1, MCP-02): an agent can read what each tool does
// and what each argument takes, and an argument the tool does not know comes back as an error before anything is read.
// Derive one class per provider and return it from Provider.
public abstract class ToolProviderConformanceTests
{
    protected abstract object Provider { get; }

    private IReadOnlyList<AgentTool> Tools => (Provider as IToolProvider)?.Tools ?? [];

    private IReadOnlyList<AgentResource> Resources => (Provider as IResourceProvider)?.Resources ?? [];

    [Fact]
    public void The_provider_offers_tools_or_resources_with_names_of_their_own()
    {
        Assert.True(Tools.Count + Resources.Count > 0, "A provider offers at least one tool or resource.");
        Assert.Equal(Tools.Count, Tools.Select(tool => tool.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Resources.Count, Resources.Select(resource => resource.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Tools, tool => Assert.True(ToolNames.IsName(tool.Name), tool.Name));
    }

    [Fact]
    public void Every_tool_and_resource_is_described_for_an_agent()
    {
        Assert.All(Tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Title), tool.Name);
            Assert.True(tool.Description.Length >= 60, $"{tool.Name} says in a sentence or two what it does and when to call it.");
        });
        Assert.All(Resources, resource => Assert.True(resource.Description.Length >= 40, resource.Name));
    }

    [Fact]
    public async Task Every_input_schema_is_an_object_that_describes_its_arguments_and_refuses_others()
    {
        foreach (var tool in Tools)
        {
            var schema = tool.InputSchema;
            Assert.Equal("object", (string?)schema["type"]);
            Assert.False((bool?)schema["additionalProperties"] ?? true, $"{tool.Name} refuses arguments it does not name.");

            var properties = schema["properties"]?.AsObject() ?? [];
            foreach (var (name, property) in properties)
            {
                Assert.False(string.IsNullOrWhiteSpace((string?)property?["description"]), $"{tool.Name}.{name} has a description.");
                var examples = property?["examples"]?.AsArray();
                Assert.True(examples is { Count: > 0 }, $"{tool.Name}.{name} has an example.");
                foreach (var example in examples!)
                {
                    Assert.Empty(await JsonSchemaValidation.ErrorsAsync(property!, example));
                }
            }

            Assert.All(schema["required"]?.AsArray() ?? [], required => Assert.True(properties.ContainsKey((string)required!), tool.Name));
        }
    }

    [Fact]
    public async Task An_unknown_argument_is_an_error_before_any_service_is_read()
    {
        var arguments = JsonDocument.Parse("""{"no_such_argument": 1}""").RootElement;

        foreach (var tool in Tools)
        {
            var result = await tool.InvokeAsync(new ToolInvocation(arguments, new NoServices()));

            Assert.False(result.IsSuccess, tool.Name);
            Assert.Contains(result.Errors, error => error.Code == ToolErrorCodes.ArgumentUnknown && error.Message.Contains("no_such_argument"));
            Assert.Equal(["errors"], result.ToJson()!.AsObject().Select(property => property.Key).Where(key => key != "hint"));
        }
    }

    [Fact]
    public void Every_resource_has_a_uri_with_a_scheme_and_a_fixed_one_matches_itself()
    {
        Assert.All(Resources, resource =>
        {
            Assert.Contains("://", resource.UriTemplate);
            if (!resource.IsTemplated)
            {
                Assert.Empty(resource.Match(resource.UriTemplate)!);
            }
        });
    }

    // A tool that reads a service before checking its arguments fails the test above with this exception.
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            throw new InvalidOperationException($"The tool read {serviceType.Name} before checking its arguments.");
        }
    }
}

public sealed class SchemaToolsConformanceTests : ToolProviderConformanceTests
{
    protected override object Provider { get; } = new SchemaTools();
}

public sealed class ChangeSetToolsConformanceTests : ToolProviderConformanceTests
{
    protected override object Provider { get; } = new ChangeSetTools();
}

public sealed class EntryToolsConformanceTests : ToolProviderConformanceTests
{
    protected override object Provider { get; } = new EntryTools();
}

public sealed class CheckToolsConformanceTests : ToolProviderConformanceTests
{
    protected override object Provider { get; } = new CheckTools();
}

public sealed class EntityToolsConformanceTests : ToolProviderConformanceTests
{
    protected override object Provider { get; } = new EntityTools();
}
