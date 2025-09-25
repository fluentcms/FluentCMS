namespace FluentCMS.Configuration;

public class OptionRegistration
{
    public string Section { get; set; } = default!;
    public Type Type { get; set; } = default!;
    public string DefaultValue { get; set; } = default!;

    // Constructor to ensure required properties are set
    public OptionRegistration()
    {
    }

    public OptionRegistration(string section, Type type, string defaultValue)
    {
        Section = section ?? throw new ArgumentNullException(nameof(section));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        DefaultValue = defaultValue ?? throw new ArgumentNullException(nameof(defaultValue));
    }

    // Implement equality for HashSet to work properly
    public override bool Equals(object? obj)
    {
        if (obj is not OptionRegistration other)
            return false;

        return Section == other.Section && Type == other.Type;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Section, Type);
    }
}
