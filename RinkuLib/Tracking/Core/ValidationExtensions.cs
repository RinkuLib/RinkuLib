using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking;

/// <summary>Provides validation extension methods.</summary>
public static class ValidationExtensions {
    /// <summary>Validates all supplied items.</summary>
    public static bool ValidateAll(this IEnumerable<IValidatable> items) {
        bool valid = true;
        foreach (IValidatable item in items) valid &= item.Validate();
        return valid;
    }

    /// <summary>Validates all supplied items with context.</summary>
    public static bool ValidateAll<TContext>(this IEnumerable<IValidatable<TContext>> items, TContext context) {
        bool valid = true;
        foreach (IValidatable<TContext> item in items) valid &= item.Validate(context);
        return valid;
    }

    /// <summary>Validates all supplied items asynchronously.</summary>
    public static async ValueTask<bool> ValidateAllAsync(this IEnumerable<IAsyncValidatable> items, CancellationToken cancellationToken = default) {
        bool valid = true;
        foreach (IAsyncValidatable item in items)
            valid &= await item.ValidateAsync(cancellationToken).ConfigureAwait(false);
        return valid;
    }

    /// <summary>Validates all supplied items with context asynchronously.</summary>
    public static async ValueTask<bool> ValidateAllAsync<TContext>(this IEnumerable<IAsyncValidatable<TContext>> items, TContext context, CancellationToken cancellationToken = default) {
        bool valid = true;
        foreach (IAsyncValidatable<TContext> item in items)
            valid &= await item.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        return valid;
    }

    /// <summary>Validates an item and returns metadata.</summary>
    public static bool Validate<TMetadata>(this IValidation<TMetadata> item, out TMetadata metadata) {
        bool valid = item.Validate();
        metadata = item.Metadata;
        return valid;
    }

    /// <summary>Validates an item with context and returns metadata.</summary>
    public static bool Validate<TContext, TMetadata>(this IValidation<TContext, TMetadata> item, TContext context, out TMetadata metadata) {
        bool valid = item.Validate(context);
        metadata = item.Metadata;
        return valid;
    }

    /// <summary>Validates asynchronously and returns metadata.</summary>
    public static async ValueTask<ValidationOutcome<TMetadata>> ValidateWithMetadataAsync<TMetadata>(
        this IAsyncValidation<TMetadata> item, CancellationToken cancellationToken = default) {
        bool valid = await item.ValidateAsync(cancellationToken).ConfigureAwait(false);
        return new(valid, item.Metadata);
    }

    /// <summary>Validates asynchronously with context and returns metadata.</summary>
    public static async ValueTask<ValidationOutcome<TMetadata>> ValidateWithMetadataAsync<TContext, TMetadata>(
        this IAsyncValidation<TContext, TMetadata> item, TContext context,
        CancellationToken cancellationToken = default) {
        bool valid = await item.ValidateAsync(context, cancellationToken).ConfigureAwait(false);
        return new(valid, item.Metadata);
    }
}
