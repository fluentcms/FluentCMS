using System.Collections.Concurrent;
using System.Reflection;
using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Core.Content.Validation;

public sealed class ValidationPipeline(SchemaModel model, IServiceProvider services)
{
    private readonly DeclarativeValidator _declarative = new(model);
    private readonly ConcurrentDictionary<(Type Owner, string Field), Type?> _propertyTypes = new();

    public async ValueTask<ValidationResult> ValidateAsync(
        SchemaType type,
        IReadOnlyDictionary<string, object?> values,
        object? entry,
        OperationContext operation,
        ValidationMode mode,
        CancellationToken cancellationToken,
        Func<ValidationContext, CancellationToken, ValueTask>? soundEntryCheck = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(operation);

        var context = new ValidationContext(type, values, operation, mode, entry);

        var visits = new List<FieldVisit>();
        _declarative.Validate(type, values, context, visits);

        foreach (var visit in visits)
        {
            await RunFieldValidatorsAsync(visit, context, cancellationToken);
        }

        if (entry is not null)
        {
            await RunEntryValidatorsAsync(entry, context, cancellationToken);
        }

        // Uniqueness (a database query) and reference integrity on publish (#2695) come through here, after the
        // validators that need no I/O, so they only run for an entry that is otherwise sound.
        if (context.Errors.Count == 0 && soundEntryCheck is not null)
        {
            await soundEntryCheck(context.ForEntry(), cancellationToken);
        }

        return context.Errors.Count == 0 ? ValidationResult.Success : new ValidationResult(context.Errors);
    }

    private async ValueTask RunFieldValidatorsAsync(FieldVisit visit, ValidationContext root, CancellationToken cancellationToken)
    {
        var fieldType = FieldClrType(visit);
        var context = root.ForField(visit.Owner, visit.Field, visit.Path, visit.Siblings);

        if (fieldType is not null && fieldType != typeof(object))
        {
            await FieldValidatorInvoker.For(fieldType).InvokeAsync(services, visit.Value, context, cancellationToken);
        }

        await FieldValidatorInvoker.For(typeof(object)).InvokeAsync(services, visit.Value, context, cancellationToken);
    }

    private async ValueTask RunEntryValidatorsAsync(object entry, ValidationContext root, CancellationToken cancellationToken)
    {
        var context = root.ForEntry();
        for (var type = entry.GetType(); type is not null && type != typeof(object); type = type.BaseType)
        {
            await EntryValidatorInvoker.For(type).InvokeAsync(services, entry, context, cancellationToken);
        }
    }

    // The type of the property the field was read from; a field of a type defined without a class has the type of its value.
    private Type? FieldClrType(FieldVisit visit)
    {
        var declared = visit.Owner.ClrType is { } owner
            ? _propertyTypes.GetOrAdd((owner, visit.Field.Name), static key => PropertyType(key.Owner, key.Field))
            : null;

        return declared ?? visit.Value?.GetType();
    }

    private static Type? PropertyType(Type owner, string fieldName)
    {
        var property = owner.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => SchemaNaming.ToCamelCase(candidate.Name) == fieldName);

        return property is null ? null : Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    }

    private abstract class FieldValidatorInvoker
    {
        private static readonly ConcurrentDictionary<Type, FieldValidatorInvoker> Cache = new();

        public static FieldValidatorInvoker For(Type fieldType) =>
            Cache.GetOrAdd(fieldType, static type => (FieldValidatorInvoker)Activator.CreateInstance(typeof(FieldValidatorInvoker<>).MakeGenericType(type))!);

        public abstract ValueTask InvokeAsync(IServiceProvider services, object? value, ValidationContext context, CancellationToken cancellationToken);
    }

    private sealed class FieldValidatorInvoker<T> : FieldValidatorInvoker
    {
        public override async ValueTask InvokeAsync(IServiceProvider services, object? value, ValidationContext context, CancellationToken cancellationToken)
        {
            // A null cannot be given to a validator of a value type.
            if (services.GetService(typeof(IEnumerable<IFieldValidator<T>>)) is not IEnumerable<IFieldValidator<T>> validators
                || (value is null && typeof(T).IsValueType)
                || (value is not null && value is not T))
            {
                return;
            }

            foreach (var validator in validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await validator.ValidateAsync((T?)value, context, cancellationToken);
            }
        }
    }

    private abstract class EntryValidatorInvoker
    {
        private static readonly ConcurrentDictionary<Type, EntryValidatorInvoker> Cache = new();

        public static EntryValidatorInvoker For(Type entryType) =>
            Cache.GetOrAdd(entryType, static type => (EntryValidatorInvoker)Activator.CreateInstance(typeof(EntryValidatorInvoker<>).MakeGenericType(type))!);

        public abstract ValueTask InvokeAsync(IServiceProvider services, object entry, ValidationContext context, CancellationToken cancellationToken);
    }

    private sealed class EntryValidatorInvoker<T> : EntryValidatorInvoker
    {
        public override async ValueTask InvokeAsync(IServiceProvider services, object entry, ValidationContext context, CancellationToken cancellationToken)
        {
            if (services.GetService(typeof(IEnumerable<IEntryValidator<T>>)) is not IEnumerable<IEntryValidator<T>> validators)
            {
                return;
            }

            foreach (var validator in validators)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await validator.ValidateAsync((T)entry, context, cancellationToken);
            }
        }
    }
}
