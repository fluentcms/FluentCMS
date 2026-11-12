using FluentCMS.Generators.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace FluentCMS.Generators;

// A location kept as values, because a Location holds its syntax tree and would defeat the generator's caching.
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

    public static LocationInfo? From(Location? location) =>
        location is { IsInSource: true, SourceTree: not null }
            ? new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span)
            : null;
}

internal sealed record FieldLocation(string Field, LocationInfo Location);

internal sealed record DiagnosticInfo(string Code, string Message, LocationInfo? Location);

// A schema class as the generator read it: what the emitter needs, and where to report what it finds.
internal sealed record TypeModel(
    TypeDescription Description,
    LocationInfo? Location,
    EquatableArray<FieldLocation> FieldLocations,
    EquatableArray<DiagnosticInfo> Diagnostics);

internal sealed record GeneratorOutput(EquatableArray<TypeModel> Types, EmitResult Result);
