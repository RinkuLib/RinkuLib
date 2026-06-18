namespace RinkuLib.Tracking;

/// <summary>
/// Represents metadata that can report whether it is in an error state.
/// </summary>
public interface IMetadataValidation {
    /// <summary>
    /// Gets a value indicating whether the metadata is in an error state.
    /// </summary>
    bool HasError { get; }
}
/// <summary>
/// Represents metadata that can validate a value
/// </summary>
/// <typeparam name="T">The type of item being validated.</typeparam>
public interface IMetadataValidation<T> : IMetadataValidation {
    /// <summary>
    /// Validates the specified item.
    /// </summary>
    /// <param name="item">The item to validate.</param>
    /// <returns>
    /// <see langword="true"/> if the item is considered valid; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Implementations may maintain internal state based on validation results.
    /// </remarks>
    bool Validate(T item);

    /// <summary>
    /// Validates the specified item using additional context from a tracking list.
    /// </summary>
    /// <param name="item">The item to validate.</param>
    /// <param name="data">The tracking list providing additional context.</param>
    /// <returns>
    /// <see langword="true"/> if the item is considered valid; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Implementations may maintain internal state based on validation results.
    /// </remarks>
    bool Validate(T item, ITrackingList<T> data);
}
/// <summary>
/// Represents metadata that can validate a value and expose an associated error when invalid.
/// </summary>
/// <typeparam name="T">The type of item being validated.</typeparam>
/// <typeparam name="TError">The type of validation error produced when invalid.</typeparam>
public interface IMetadataValidation<T, TError> : IMetadataValidation<T> {
    /// <summary>
    /// Gets the validation error associated with the metadata, if any.
    /// </summary>
    TError? Error { get; }
}
