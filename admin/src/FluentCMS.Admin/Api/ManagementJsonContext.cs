using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Api;

// Every record the admin reads or writes, so that the app trims (spec 2.7). Entries stay JsonObject.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiProblem))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(ItemList<MySiteInfo>))]
[JsonSerializable(typeof(MyAccessInfo))]
[JsonSerializable(typeof(SchemaDescription))]
[JsonSerializable(typeof(ItemList<PluginInfo>))]
[JsonSerializable(typeof(ContentEntry))]
[JsonSerializable(typeof(ContentPage))]
[JsonSerializable(typeof(PublishAnswer))]
[JsonSerializable(typeof(ScheduleAnswer))]
[JsonSerializable(typeof(ItemList<MySession>))]
[JsonSerializable(typeof(MyTwoFactor))]
[JsonSerializable(typeof(AuthenticatorSetup))]
[JsonSerializable(typeof(RecoveryCodeList))]
[JsonSerializable(typeof(ItemList<MyLogin>))]
[JsonSerializable(typeof(MyPreferences))]
[JsonSerializable(typeof(JsonNode))]
internal sealed partial class ManagementJsonContext : JsonSerializerContext
{
}
