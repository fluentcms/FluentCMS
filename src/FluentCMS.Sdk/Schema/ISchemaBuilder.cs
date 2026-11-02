using System.Reflection;

namespace FluentCMS.Schema;

/// <summary>
/// Collects the types of the schema from classes with attributes, from calls that define a type without a class,
/// and from <c>schema.json</c> files. All three produce the same <see cref="SchemaModel"/>.
/// </summary>
public interface ISchemaBuilder
{
    /// <summary>
    /// Adds a class marked as a collection, singleton, component or block. The components and blocks its fields
    /// use are added with it; the content types its relations point to are not.
    /// </summary>
    /// <typeparam name="T">The class.</typeparam>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddContentType<T>();

    /// <summary>Adds a class marked as a collection, singleton, component or block.</summary>
    /// <param name="type">The class.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddContentType(Type type);

    /// <summary>Adds every class in an assembly that is marked as a collection, singleton, component or block.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddContentTypesFromAssembly(Assembly assembly);

    /// <summary>Defines a collection without a class.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <param name="configure">Declares the fields.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddCollection(string name, Action<ITypeBuilder> configure);

    /// <summary>Defines a singleton without a class.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <param name="configure">Declares the fields.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddSingleton(string name, Action<ITypeBuilder> configure);

    /// <summary>Defines a component without a class.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <param name="configure">Declares the fields.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddComponent(string name, Action<ITypeBuilder> configure);

    /// <summary>Defines a block without a class.</summary>
    /// <param name="name">The type name, in lower kebab-case.</param>
    /// <param name="configure">Declares the fields.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddBlock(string name, Action<ITypeBuilder> configure);

    /// <summary>Adds the types of a <c>schema.json</c> file.</summary>
    /// <param name="path">The path of the file, absolute or relative to the current directory.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder LoadSchemaFile(string path);

    /// <summary>Adds the types of a <c>schema.json</c> document.</summary>
    /// <param name="json">The document.</param>
    /// <returns>This builder.</returns>
    ISchemaBuilder AddSchemaJson(string json);
}
