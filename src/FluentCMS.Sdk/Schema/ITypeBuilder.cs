namespace FluentCMS.Schema;

/// <summary>Declares the fields of a type defined without a class.</summary>
public interface ITypeBuilder
{
    /// <summary>Sets whether entries have one version per locale. Only collections and singletons can be localized.</summary>
    /// <param name="localized">Whether the type is localized.</param>
    /// <returns>This builder.</returns>
    ITypeBuilder Localized(bool localized = true);

    /// <summary>Sets whether entries belong to one site or are shared by all sites. Only collections and singletons have a scope.</summary>
    /// <param name="scope">The scope of the type.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The type is a component or a block and <paramref name="scope"/> is not <see cref="ContentScope.Site"/>.</exception>
    ITypeBuilder Scope(ContentScope scope);

    /// <summary>Declares a field.</summary>
    /// <param name="name">The field name, in camelCase.</param>
    /// <param name="fieldType">The name of the field type, such as <c>text</c> or <c>richText</c>.</param>
    /// <param name="configure">Sets the options of the field.</param>
    /// <returns>This builder.</returns>
    ITypeBuilder Field(string name, string fieldType, Action<IFieldBuilder>? configure = null);
}
