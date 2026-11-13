using FluentCMS.Publishing;

namespace FluentCMS.Schema;

/// <summary>Declares the fields of a type defined without a class.</summary>
public interface ITypeBuilder
{
    /// <summary>The same as <c>Localized(true)</c>.</summary>
    /// <returns>This builder.</returns>
    ITypeBuilder Localized() => Localized(true);

    /// <summary>Sets whether entries have one version per locale. Only collections and singletons can be localized.</summary>
    /// <param name="localized">Whether the type is localized.</param>
    /// <returns>This builder.</returns>
    ITypeBuilder Localized(bool localized);

    /// <summary>Sets whether entries belong to one site or are shared by all sites. Only collections and singletons have a scope.</summary>
    /// <param name="scope">The scope of the type.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The type is a component or a block and <paramref name="scope"/> is not <see cref="ContentScope.Site"/>.</exception>
    ITypeBuilder Scope(ContentScope scope);

    /// <summary>Sets how entries are published, how many versions are kept and how long the trash holds them. Only collections and singletons are published.</summary>
    /// <param name="options">The policy; what it does not set keeps the default.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentException">The type is a component or a block.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="PublishPolicyOptions.MaxVersions"/> is less than 1, or <see cref="PublishPolicyOptions.TrashRetentionDays"/> is outside 1 to <see cref="PublishPolicyAttribute.MaxTrashRetentionDays"/>.</exception>
    ITypeBuilder PublishPolicy(PublishPolicyOptions options);

    /// <summary>Declares a field with the defaults of its type.</summary>
    /// <param name="name">The field name, in camelCase.</param>
    /// <param name="fieldType">The name of the field type, such as <c>text</c> or <c>richText</c>.</param>
    /// <returns>This builder.</returns>
    ITypeBuilder Field(string name, string fieldType) => Field(name, fieldType, static _ => { });

    /// <summary>Declares a field.</summary>
    /// <param name="name">The field name, in camelCase.</param>
    /// <param name="fieldType">The name of the field type, such as <c>text</c> or <c>richText</c>.</param>
    /// <param name="configure">Sets the options of the field.</param>
    /// <returns>This builder.</returns>
    ITypeBuilder Field(string name, string fieldType, Action<IFieldBuilder> configure);
}
