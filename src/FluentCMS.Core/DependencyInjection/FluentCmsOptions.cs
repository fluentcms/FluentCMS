using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FluentCMS.Core.DependencyInjection;

// Binds a section under FluentCms onto an options instance, which may already hold what the host set in code. A key the
// configuration sets wins over the code; the result is validated when the host starts, and every error names its key,
// which is why the messages of a Validate method start with the property they are about.
internal static class FluentCmsOptions
{
    public const string Section = "FluentCms";

    public static void AddFluentCmsOptions<T>(this IServiceCollection services, string section, string code, T instance, Action<T>? validate = null)
        where T : class
    {
        var path = $"{Section}:{section}";

        services.AddSingleton<IOptionsFactory<T>>(provider => new InstanceOptionsFactory<T>(
            instance,
            provider.GetServices<IConfigureOptions<T>>(),
            provider.GetServices<IPostConfigureOptions<T>>(),
            provider.GetServices<IValidateOptions<T>>()));
        services.AddSingleton<IValidateOptions<T>>(new SectionValidator<T>(path, code, validate));
        services.AddOptions<T>()
            .Configure<IServiceProvider>((options, provider) => Bind(provider.GetService<IConfiguration>()?.GetSection(path), options, code))
            .ValidateOnStart();
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<T>>().Value);
    }

    private static void Bind<T>(IConfigurationSection? section, T options, string code)
    {
        try
        {
            section?.Bind(options);
        }
        catch (InvalidOperationException exception)
        {
            throw new FluentCmsConfigurationException(code, exception.Message);
        }
    }

    // Startup validation and IOptions<T> each create the options, and binding twice would add a list's items twice.
    private sealed class InstanceOptionsFactory<T> : IOptionsFactory<T>
        where T : class
    {
        private readonly InstanceFactory _factory;
        private readonly Lazy<T> _default;

        public InstanceOptionsFactory(T instance, IEnumerable<IConfigureOptions<T>> setups, IEnumerable<IPostConfigureOptions<T>> postConfigures, IEnumerable<IValidateOptions<T>> validations)
        {
            _factory = new InstanceFactory(instance, setups, postConfigures, validations);
            _default = new Lazy<T>(() => _factory.Create(Options.DefaultName));
        }

        public T Create(string name) => name == Options.DefaultName ? _default.Value : _factory.Create(name);

        private sealed class InstanceFactory(T instance, IEnumerable<IConfigureOptions<T>> setups, IEnumerable<IPostConfigureOptions<T>> postConfigures, IEnumerable<IValidateOptions<T>> validations)
            : OptionsFactory<T>(setups, postConfigures, validations)
        {
            protected override T CreateInstance(string name) => name == Options.DefaultName ? instance : base.CreateInstance(name);
        }
    }

    // Throws rather than returning a failure, so that a bad option stops the host with its FCMS code like every other
    // configuration error.
    private sealed class SectionValidator<T>(string path, string code, Action<T>? validate) : IValidateOptions<T>
        where T : class
    {
        public ValidateOptionsResult Validate(string? name, T options)
        {
            if (name != Options.DefaultName)
            {
                return ValidateOptionsResult.Skip;
            }

            List<ValidationResult> results = [];
            Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
            List<(string Code, string Message)> failures = [.. results.Select(result => (code, $"{path}:{result.MemberNames.FirstOrDefault()}: {result.ErrorMessage}"))];

            try
            {
                validate?.Invoke(options);
            }
            catch (FluentCmsConfigurationException exception)
            {
                failures.Add((exception.Code, $"{path}:{exception.Detail}"));
            }

            if (failures.Count == 0)
            {
                return ValidateOptionsResult.Success;
            }

            var message = string.Join("; ", failures.Select((failure, index) => index == 0 ? failure.Message : $"{failure.Code}: {failure.Message}"));
            throw new FluentCmsConfigurationException(failures[0].Code, message);
        }
    }
}
