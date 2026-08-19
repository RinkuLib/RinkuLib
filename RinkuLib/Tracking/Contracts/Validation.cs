using System;
using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking;

/// <summary>Contains validation status and returned metadata.</summary>
public readonly record struct ValidationOutcome<TMetadata> {
    /// <summary>Creates a validation outcome.</summary>
    public ValidationOutcome(bool isValid, TMetadata metadata) {
        IsValid = isValid;
        Metadata = metadata;
    }

    /// <summary>Gets whether validation succeeded.</summary>
    public bool IsValid { get; }
    /// <summary>Gets validation metadata.</summary>
    public TMetadata Metadata { get; }
}

/// <summary>Provides validation without a context value.</summary>
public interface IValidatable {
    /// <summary>Returns whether the value is valid.</summary>
    bool Validate();
}

/// <summary>Provides validation with a caller-supplied context.</summary>
public interface IValidatable<in TContext> {
    /// <summary>Returns whether the value is valid for the supplied context.</summary>
    bool Validate(TContext context);
}

/// <summary>Provides asynchronous validation without a context value.</summary>
public interface IAsyncValidatable {
    /// <summary>Returns whether the value is valid.</summary>
    ValueTask<bool> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provides asynchronous validation with a caller-supplied context.</summary>
public interface IAsyncValidatable<in TContext> {
    /// <summary>Returns whether the value is valid for the supplied context.</summary>
    ValueTask<bool> ValidateAsync(TContext context, CancellationToken cancellationToken = default);
}

// Validation itself does not require metadata. These only name useful intersections.
/// <summary>Combines synchronous validation and metadata reading.</summary>
public interface IValidation<out TMetadata> : IValidatable, IMetadataReader<TMetadata> { }

/// <summary>Combines contextual synchronous validation and metadata reading.</summary>
public interface IValidation<in TContext, out TMetadata> : IValidatable<TContext>, IMetadataReader<TMetadata> { }

/// <summary>Combines contextual and context-free synchronous validation.</summary>
public interface IDualValidation<in TContext, out TMetadata> :
    IValidation<TMetadata>,
    IValidation<TContext, TMetadata> { }

/// <summary>Combines asynchronous validation and metadata reading.</summary>
public interface IAsyncValidation<out TMetadata> : IAsyncValidatable, IMetadataReader<TMetadata> { }

/// <summary>Combines contextual asynchronous validation and metadata reading.</summary>
public interface IAsyncValidation<in TContext, out TMetadata> :
    IAsyncValidatable<TContext>,
    IMetadataReader<TMetadata> { }

/// <summary>Combines contextual and context-free asynchronous validation.</summary>
public interface IAsyncDualValidation<in TContext, out TMetadata> :
    IAsyncValidation<TMetadata>,
    IAsyncValidation<TContext, TMetadata> { }

// Useful when one item intentionally supports both sync and async forms.
/// <summary>Combines synchronous and asynchronous validation.</summary>
public interface ISyncAsyncValidation<out TMetadata> :
    IValidation<TMetadata>,
    IAsyncValidation<TMetadata> { }

/// <summary>Combines contextual synchronous and asynchronous validation.</summary>
public interface ISyncAsyncValidation<in TContext, out TMetadata> :
    IValidation<TContext, TMetadata>,
    IAsyncValidation<TContext, TMetadata> { }

/// <summary>Combines all supported validation forms.</summary>
public interface ICompleteValidation<in TContext, out TMetadata> :
    IDualValidation<TContext, TMetadata>,
    IAsyncDualValidation<TContext, TMetadata>,
    ISyncAsyncValidation<TMetadata>,
    ISyncAsyncValidation<TContext, TMetadata> { }
