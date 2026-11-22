using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Api;

// Every record the admin reads or writes, so that the app trims (spec 2.7). Entries stay JsonObject. Metadata only: the
// serialization fast path would be in the first download for the few records the admin writes (spec 7, #3354).
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ApiProblem))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(ItemList<MySiteInfo>))]
[JsonSerializable(typeof(MyAccessInfo))]
[JsonSerializable(typeof(SchemaDescription))]
[JsonSerializable(typeof(ItemList<PluginInfo>))]
[JsonSerializable(typeof(ContentEntry))]
[JsonSerializable(typeof(ContentPage))]
[JsonSerializable(typeof(PublishAnswer))]
[JsonSerializable(typeof(BulkAnswer))]
[JsonSerializable(typeof(ItemList<EntryVersionInfo>))]
[JsonSerializable(typeof(VersionDiffInfo))]
[JsonSerializable(typeof(ScheduleAnswer))]
[JsonSerializable(typeof(PreviewTokenAnswer))]
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
