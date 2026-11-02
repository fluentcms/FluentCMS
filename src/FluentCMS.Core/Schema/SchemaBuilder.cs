using System.Collections.Frozen;
using System.Reflection;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema;

public sealed class SchemaBuilder : ISchemaBuilder
{
    private readonly List<TypeDraft> _types = [];
    private readonly List<SchemaError> _errors = [];
    private readonly HashSet<Type> _classes = [];
    private readonly IReadOnlySet<string> _fieldTypes;

    public SchemaBuilder()
        : this([])
    {
    }

    internal SchemaBuilder(IEnumerable<string> additionalFieldTypes)
    {
        _fieldTypes = KnownFieldTypes.Core.Union(additionalFieldTypes).ToFrozenSet(StringComparer.Ordinal);
    }

    public SchemaBuilder AddContentType<T>() => AddContentType(typeof(T));

    public SchemaBuilder AddContentType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        new AttributeSchemaReader(this).Read(type);
        return this;
    }

    public SchemaBuilder AddContentTypesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var reader = new AttributeSchemaReader(this);
        foreach (var type in LoadTypes(assembly).Where(AttributeSchemaReader.IsSchemaClass).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            reader.Read(type);
        }

        return this;
    }

    public SchemaBuilder AddCollection(string name, Action<ITypeBuilder> configure) =>
        AddType(name, SchemaTypeKind.Collection, null, configure);

    public SchemaBuilder AddSingleton(string name, Action<ITypeBuilder> configure) =>
        AddType(name, SchemaTypeKind.Singleton, null, configure);

    public SchemaBuilder AddComponent(string name, Action<ITypeBuilder> configure) =>
        AddType(name, SchemaTypeKind.Component, null, configure);

    public SchemaBuilder AddBlock(string name, Action<ITypeBuilder> configure) =>
        AddType(name, SchemaTypeKind.Block, null, configure);

    public SchemaBuilder LoadSchemaFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        new SchemaJsonReader(this).Read(File.ReadAllText(path), path);
        return this;
    }

    public SchemaBuilder AddSchemaJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        new SchemaJsonReader(this).Read(json, "schema.json");
        return this;
    }

    public SchemaModel Build()
    {
        var errors = new List<SchemaError>(_errors);
        errors.AddRange(new SchemaValidator(_fieldTypes).Validate(_types));

        if (errors.Count > 0)
        {
            throw new SchemaValidationException(errors);
        }

        return SchemaModelFactory.Create(_types);
    }

    ISchemaBuilder ISchemaBuilder.AddContentType<T>() => AddContentType<T>();

    ISchemaBuilder ISchemaBuilder.AddContentType(Type type) => AddContentType(type);

    ISchemaBuilder ISchemaBuilder.AddContentTypesFromAssembly(Assembly assembly) => AddContentTypesFromAssembly(assembly);

    ISchemaBuilder ISchemaBuilder.AddCollection(string name, Action<ITypeBuilder> configure) => AddCollection(name, configure);

    ISchemaBuilder ISchemaBuilder.AddSingleton(string name, Action<ITypeBuilder> configure) => AddSingleton(name, configure);

    ISchemaBuilder ISchemaBuilder.AddComponent(string name, Action<ITypeBuilder> configure) => AddComponent(name, configure);

    ISchemaBuilder ISchemaBuilder.AddBlock(string name, Action<ITypeBuilder> configure) => AddBlock(name, configure);

    ISchemaBuilder ISchemaBuilder.LoadSchemaFile(string path) => LoadSchemaFile(path);

    ISchemaBuilder ISchemaBuilder.AddSchemaJson(string json) => AddSchemaJson(json);

    internal SchemaBuilder AddType(string name, SchemaTypeKind kind, Type? clrType, Action<ITypeBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);

        var draft = new TypeDraft(name, kind, clrType);
        _types.Add(draft);
        configure(new TypeBuilder(draft));
        return this;
    }

    internal bool Track(Type type) => _classes.Add(type);

    internal void Report(string code, string message) => _errors.Add(new SchemaError(code, message));

    private static IEnumerable<Type> LoadTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }
}
