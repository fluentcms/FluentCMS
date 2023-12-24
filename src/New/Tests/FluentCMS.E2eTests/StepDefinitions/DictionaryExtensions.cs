namespace FluentCMS.E2eTests.StepDefinitions;

public static class DictionaryExtensions
{
    // GetOrCreate
    public static T GetOrCreate<T>(this IDictionary<string, T> dictionary, string key, T Object)
    {
        if (dictionary.TryGetValue(key, out T value))
        {
            return value;
        }
        dictionary.Add(key, Object);
        return Object;
    }
}
