using System.Globalization;
using System.Text.RegularExpressions;
using FluentCMS.Content;
using FluentCMS.Media;

namespace FluentCMS.Plugins.Media;

// The parameters of a transform URL and of a preset, as the query names them: w, h, fit, q, f, fx and fy, and preset. The
// canonical string that a signature covers is the asset's id and the parameters as the URL gives them, sorted by name and
// escaped, so that two URLs sign alike only when they carry the same parameters.
internal static partial class ImageQuery
{
    public const string Width = "w";
    public const string Height = "h";
    public const string Fit = "fit";
    public const string Quality = "q";
    public const string Format = "f";
    public const string FocalX = "fx";
    public const string FocalY = "fy";
    public const string Preset = "preset";
    public const string Signature = "sig";

    // Read by the host to choose the site, so a URL may carry it; it changes nothing that the signature would cover.
    public const string Site = "site";

    public const int PresetNameMaxLength = 64;

    private static readonly string[] Names = [Width, Height, Fit, Quality, Format, FocalX, FocalY, Preset];

    private static readonly Dictionary<string, string> Members = new(StringComparer.Ordinal)
    {
        [Width] = "width",
        [Height] = "height",
        [Fit] = "fit",
        [Quality] = "quality",
        [Format] = "format",
        [FocalX] = "focalPoint",
        [Preset] = "preset",
    };

    private static readonly Dictionary<string, ImageFit> Fits = new(StringComparer.Ordinal)
    {
        ["contain"] = ImageFit.Contain,
        ["cover"] = ImageFit.Cover,
        ["fill"] = ImageFit.Fill,
    };

    private static readonly Dictionary<string, MediaImageFormat> Formats = new(StringComparer.Ordinal)
    {
        ["auto"] = MediaImageFormat.Auto,
        ["webp"] = MediaImageFormat.Webp,
        ["avif"] = MediaImageFormat.Avif,
        ["jpeg"] = MediaImageFormat.Jpeg,
        ["png"] = MediaImageFormat.Png,
    };

    public static bool IsValidPresetName(string? name) => name is not null && PresetName().IsMatch(name);

    // The transform parameters of a query and its signature; a name given twice, or one that is no parameter, is refused.
    public static (IReadOnlyList<KeyValuePair<string, string>> Parameters, string? Signature) Split(IEnumerable<KeyValuePair<string, string?>> query)
    {
        List<KeyValuePair<string, string>> parameters = [];
        string? signature = null;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (given, value) in query)
        {
            var name = given.ToLowerInvariant();
            if (!seen.Add(name))
            {
                throw MediaService.Invalid(name, $"The parameter '{name}' is given more than once.");
            }

            if (name == Signature)
            {
                signature = value;
            }
            else if (Names.Contains(name))
            {
                parameters.Add(new(name, value ?? ""));
            }
            else if (name != Site)
            {
                throw MediaService.Invalid(name, $"'{name}' is not a parameter of an image: {string.Join(", ", Names)} and {Signature}.");
            }
        }

        return (parameters, signature);
    }

    public static string Canonical(Guid id, IEnumerable<KeyValuePair<string, string>> parameters) =>
        $"{id:D}?{string.Join('&', parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"))}";

    public static MediaImageOptions Parse(IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        List<ValidationError> errors = [];
        var options = new MediaImageOptions();
        double? x = null;
        double? y = null;
        foreach (var (name, value) in parameters)
        {
            switch (name)
            {
                case Width:
                    options = options with { Width = Number(errors, name, value) };
                    break;
                case Height:
                    options = options with { Height = Number(errors, name, value) };
                    break;
                case Quality:
                    options = options with { Quality = Number(errors, name, value) };
                    break;
                case Fit:
                    options = options with { Fit = Fits.TryGetValue(value, out var fit) ? fit : Error<ImageFit?>(errors, name, $"A fit is {string.Join(", ", Fits.Keys)}.") };
                    break;
                case Format:
                    options = options with { Format = Formats.TryGetValue(value, out var format) ? format : Error<MediaImageFormat?>(errors, name, $"A format is {string.Join(", ", Formats.Keys)}.") };
                    break;
                case FocalX:
                    x = Fraction(errors, name, value);
                    break;
                case FocalY:
                    y = Fraction(errors, name, value);
                    break;
                case Preset:
                    options = options with { Preset = value };
                    break;
            }
        }

        if (x is { } focalX && y is { } focalY)
        {
            options = options with { FocalPoint = new MediaFocalPoint(focalX, focalY) };
        }

        Check(errors, options, asQuery: true, allowPreset: true);
        if (errors.Count == 0 && parameters.Any(pair => pair.Key is FocalX) != parameters.Any(pair => pair.Key is FocalY))
        {
            errors.Add(new ValidationError(parameters.Any(pair => pair.Key is FocalX) ? FocalY : FocalX, MediaErrorCodes.InvalidField, $"{FocalX} and {FocalY} are given together."));
        }

        return errors.Count == 0 ? options : throw new ValidationException(errors);
    }

    // The query of options, in the order of Names; what is left null is left out.
    public static List<KeyValuePair<string, string>> Of(MediaImageOptions options)
    {
        List<KeyValuePair<string, string>> pairs = [];
        Add(pairs, Width, options.Width?.ToString(CultureInfo.InvariantCulture));
        Add(pairs, Height, options.Height?.ToString(CultureInfo.InvariantCulture));
        Add(pairs, Fit, options.Fit is { } fit ? Fits.Single(item => item.Value == fit).Key : null);
        Add(pairs, Quality, options.Quality?.ToString(CultureInfo.InvariantCulture));
        Add(pairs, Format, options.Format is { } format ? Formats.Single(item => item.Value == format).Key : null);
        Add(pairs, FocalX, options.FocalPoint?.X.ToString(CultureInfo.InvariantCulture));
        Add(pairs, FocalY, options.FocalPoint?.Y.ToString(CultureInfo.InvariantCulture));
        Add(pairs, Preset, options.Preset);
        return pairs;
    }

    // The values of the query win over those of the preset.
    public static MediaImageOptions Merge(MediaImageOptions preset, MediaImageOptions query) => new()
    {
        Width = query.Width ?? preset.Width,
        Height = query.Height ?? preset.Height,
        Fit = query.Fit ?? preset.Fit,
        Quality = query.Quality ?? preset.Quality,
        Format = query.Format ?? preset.Format,
        FocalPoint = query.FocalPoint ?? preset.FocalPoint,
    };

    // Each parameter is one operation, and a focal point one.
    public static int Operations(MediaImageOptions options) =>
        new object?[] { options.Width, options.Height, options.Fit, options.Quality, options.Format, options.FocalPoint }.Count(value => value is not null);

    // The ranges a value has whatever the site's limits: those are checked when a request or a preset meets them. An error
    // names the parameter of the query, or the member of MediaImageOptions as JSON names it.
    public static void Check(List<ValidationError> errors, MediaImageOptions options, bool asQuery, bool allowPreset)
    {
        string Path(string parameter) => asQuery ? parameter : Members[parameter];

        if (options.Width is < 1)
        {
            errors.Add(new ValidationError(Path(Width), MediaErrorCodes.InvalidField, "A width is at least 1."));
        }

        if (options.Height is < 1)
        {
            errors.Add(new ValidationError(Path(Height), MediaErrorCodes.InvalidField, "A height is at least 1."));
        }

        if (options.Quality is < 1 or > 100)
        {
            errors.Add(new ValidationError(Path(Quality), MediaErrorCodes.InvalidField, "A quality is from 1 to 100."));
        }

        if (options.Fit is { } fit && !Enum.IsDefined(fit))
        {
            errors.Add(new ValidationError(Path(Fit), MediaErrorCodes.InvalidField, $"A fit is {string.Join(", ", Fits.Keys)}."));
        }

        if (options.Format is { } format && !Enum.IsDefined(format))
        {
            errors.Add(new ValidationError(Path(Format), MediaErrorCodes.InvalidField, $"A format is {string.Join(", ", Formats.Keys)}."));
        }

        if (options.FocalPoint is { } focal && !(focal.X is >= 0 and <= 1 && focal.Y is >= 0 and <= 1))
        {
            errors.Add(new ValidationError(Path(FocalX), MediaErrorCodes.InvalidField, "A focal point has x and y from 0 to 1."));
        }

        if (options.Preset is { } preset && (!allowPreset || !IsValidPresetName(preset)))
        {
            errors.Add(new ValidationError(Path(Preset), MediaErrorCodes.InvalidField, allowPreset
                ? $"A preset name has from 1 to {PresetNameMaxLength} lower-case letters, digits and hyphens, and starts with a letter or digit."
                : "A preset names no other preset."));
        }
    }

    private static void Add(List<KeyValuePair<string, string>> pairs, string name, string? value)
    {
        if (value is not null)
        {
            pairs.Add(new(name, value));
        }
    }

    private static int? Number(List<ValidationError> errors, string name, string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : Error<int?>(errors, name, $"The {name} is a whole number.");

    private static double? Fraction(List<ValidationError> errors, string name, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction) && fraction is >= 0 and <= 1
            ? fraction
            : Error<double?>(errors, name, $"The {name} is a number from 0 to 1.");

    private static T? Error<T>(List<ValidationError> errors, string name, string message)
    {
        errors.Add(new ValidationError(name, MediaErrorCodes.InvalidField, message));
        return default;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PresetName();
}
