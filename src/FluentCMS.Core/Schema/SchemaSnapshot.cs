using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

// The canonical JSON of a model and its SHA-256 as lowercase hex, which is the schema hash. Types are ordered by name,
// fields by declaration, and neither runtime values nor the class a type was read from are in it, so two builds of one
// schema give the same bytes. Computed once per instance, because options are rebuilt per scope.
internal sealed record SchemaSnapshot(string Json, string Hash)
{
    private static readonly ConditionalWeakTable<SchemaModel, SchemaSnapshot> Snapshots = [];

    public static SchemaSnapshot Of(SchemaModel schema) => Snapshots.GetValue(schema, static model => Create(SchemaJsonWriter.Write(model)));

    public static SchemaSnapshot Create(string json) => new(json, HashOf(json));

    public static string HashOf(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
