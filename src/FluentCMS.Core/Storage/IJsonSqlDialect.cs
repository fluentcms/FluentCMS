using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Core.Storage;

// Builds the JSON filters of the query compiler as LINQ expressions over a provider's EF function mappings,
// limited to what Q7 supports. Each method returns a bool expression, so filters compose with And, Or and Not.
//
// A json argument is a string expression holding JSON: EF.Property<string>(row, "sections"), or the block that
// AnyBlock hands to its predicate. A value argument is the compiler's parameter expression; bind it by reading
// a member of a holder object, because a bare constant becomes a SQL literal.
public interface IJsonSqlDialect
{
    // Declares the functions the expressions below call. CmsDbContext applies this to its model.
    void ConfigureFunctions(ModelBuilder modelBuilder);

    // The block list holds a block of this type. blockType is a string expression.
    Expression HasBlock(Expression json, Expression blockType);

    // The block list holds a block of this type that also satisfies the predicate, so that every condition
    // applies to the same block. The predicate receives the block and calls the Scalar and Array methods on it.
    Expression AnyBlock(Expression json, Expression blockType, Func<Expression, Expression> predicate);

    // The value at the top-level key has this type and equals the value, whose CLR type is JsonScalar.ClrType(type).
    // A JSON number never equals the text '12'.
    Expression ScalarEquals(Expression json, string field, JsonScalarType type, Expression value);

    // The value at the top-level key is JSON text that contains the string value, case-sensitively.
    Expression ScalarContains(Expression json, string field, Expression value);

    // The value at the top-level key is an array holding the string value as an element.
    Expression ArrayContains(Expression json, string field, Expression value);

    // The JSON is itself an array holding the string value as an element: a multi select column.
    Expression ListContains(Expression json, Expression value);
}
