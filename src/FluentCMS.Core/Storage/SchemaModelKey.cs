using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Core.Schema;
using FluentCMS.Schema;

namespace FluentCMS.Core.Storage;

// The SHA-256 of the canonical JSON of a model, so equal schemas share one EF model. Computed once per
// instance, because options are rebuilt per scope. The schema hash of #2687 replaces it.
internal static class SchemaModelKey
{
    private static readonly ConditionalWeakTable<SchemaModel, string> Keys = [];

    public static string Of(SchemaModel schema) =>
        Keys.GetValue(schema, static model => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SchemaJsonWriter.Write(model)))));
}
