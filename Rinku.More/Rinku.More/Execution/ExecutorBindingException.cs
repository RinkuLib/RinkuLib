namespace Rinku;

/// <summary>Identifies why an executor operation could not bind.</summary>
public enum ExecutorBindingFailure : byte
{
    /// <summary>No compatible implementation was found.</summary>
    MissingImplementation,
    /// <summary>A required input has no materialized value.</summary>
    UnavailableInput,
    /// <summary>An operation cannot accept a known-null value.</summary>
    NullNotAccepted,
    /// <summary>A previously bound variable would require a different type.</summary>
    TypeMismatch
}

/// <summary>Reports an operation that cannot bind to the current variable state and provider set.</summary>
public class ExecutorBindingException : InvalidOperationException
{
    public ExecutorBindingException(ExecutorBindingFailure failure, string message) : base(message) => Failure = failure;
    /// <summary>Gets the binding failure category.</summary>
    public ExecutorBindingFailure Failure
    {
        get;
    }
}
