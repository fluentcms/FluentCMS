using System.Text.Json.Nodes;

namespace FluentCMS.Core.Tools;

// Builds tool input schemas (JSON Schema, draft 2020-12) the way every tool states them: an object that names and describes
// each argument, with an example, and refuses any other argument.
public static class ToolSchemas
{
    public static JsonObject Object(params ToolArgument[] arguments)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var argument in arguments)
        {
            var schema = argument.Schema.DeepClone().AsObject();
            schema["description"] = argument.Description;
            properties[argument.Name] = schema;

            if (argument.Required)
            {
                required.Add(argument.Name);
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    public static ToolArgument String(string name, string description, string example, bool required = true)
    {
        return new ToolArgument(name, description, required, new JsonObject { ["type"] = "string", ["minLength"] = 1, ["examples"] = new JsonArray(example) });
    }

    public static ToolArgument Uuid(string name, string description, bool required = true)
    {
        return new ToolArgument(
            name,
            description,
            required,
            new JsonObject { ["type"] = "string", ["format"] = "uuid", ["examples"] = new JsonArray("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e") });
    }

    public static ToolArgument PositiveInteger(string name, string description, int example, bool required = true)
    {
        return new ToolArgument(name, description, required, new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["examples"] = new JsonArray(example) });
    }
}

public sealed record ToolArgument(string Name, string Description, bool Required, JsonObject Schema);
