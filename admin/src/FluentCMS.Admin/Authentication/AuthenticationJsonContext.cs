using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentCMS.Admin.Authentication;

// The records of the sign-in, in the snake_case names of OAuth and OpenID Connect, so that the app trims.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(OpenIdConfiguration))]
[JsonSerializable(typeof(TokenAnswer))]
[JsonSerializable(typeof(TokenError))]
[JsonSerializable(typeof(StoredSession))]
[JsonSerializable(typeof(PendingSignIn))]
[JsonSerializable(typeof(List<StoredDraft>))]
internal sealed partial class AuthenticationJsonContext : JsonSerializerContext
{
}
