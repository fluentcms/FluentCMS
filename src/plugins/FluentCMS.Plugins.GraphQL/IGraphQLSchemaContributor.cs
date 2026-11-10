using HotChocolate;

namespace FluentCMS.Plugins.GraphQL;

/// <summary>
/// Adds types and fields to the GraphQL schema. A plugin registers its contributor as a singleton,
/// <c>plugin.Services.AddSingleton&lt;IGraphQLSchemaContributor, T&gt;()</c>, and the GraphQL plugin calls it once, when it
/// builds the schema at start, after the types of the content schema. A field joins <c>Query</c> or <c>Mutation</c> through
/// an <c>ObjectTypeExtension</c> of that name. What a contributed field returns is the contributor's to protect: its
/// resolver runs inside the operation of the request and checks its permissions with <c>IPermissionChecker</c>.
/// </summary>
public interface IGraphQLSchemaContributor
{
    /// <summary>Adds the contributor's types to the schema.</summary>
    /// <param name="schema">The builder of the schema.</param>
    void Configure(ISchemaBuilder schema);
}
