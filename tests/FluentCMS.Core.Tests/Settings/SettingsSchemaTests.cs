using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Core.Settings;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Settings;

public enum Channel
{
    Email,
    TextMessage,
}

public sealed class ChannelSettings
{
    [Description("Where notices go.")]
    [JsonConverter(typeof(ChannelConverter))]
    public Channel Channel { get; set; } = Channel.TextMessage;
}

// A converter of the class's own, such as one that also reads a name the enum no longer has.
public sealed class ChannelConverter : JsonConverter<Channel>
{
    public override Channel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Enum.Parse<Channel>(reader.GetString()!, ignoreCase: true);

    public override void Write(Utf8JsonWriter writer, Channel value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

public sealed class SettingsSchemaTests
{
    // System.Text.Json describes no custom converter, which would leave the property open; the schema lists the values of
    // the enum instead, with the default and the attributes as for any other property.
    [Fact]
    public void AnEnumReadByAConverterOfItsOwnListsItsValues()
    {
        var channel = new SettingsDescriptor("notices", typeof(ChannelSettings)).CreateSchema()["properties"]!["channel"]!.AsObject();

        channel["enum"]!.AsArray().Select(value => value!.GetValue<string>()).ShouldBe(["email", "textMessage"]);
        channel["default"]!.GetValue<string>().ShouldBe("textMessage");
        channel["description"]!.GetValue<string>().ShouldBe("Where notices go.");
    }
}
