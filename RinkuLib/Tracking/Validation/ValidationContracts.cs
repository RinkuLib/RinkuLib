using System.Threading;
using System.Threading.Tasks;

namespace Rinku.Tracking;

/// <summary>Validates an item.</summary>
public interface IValidatable
{
    /// <summary>Validates the item.</summary>
    bool Validate();
}

/// <summary>Validates an item with caller data.</summary>
public interface IValidatable<in TContext>
{
    /// <summary>Validates the item with caller data.</summary>
    bool Validate(TContext context);
}

/// <summary>Validates an item asynchronously.</summary>
public interface IAsyncValidatable
{
    /// <summary>Validates the item asynchronously.</summary>
    ValueTask<bool> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Validates an item asynchronously with caller data.</summary>
public interface IAsyncValidatable<in TContext>
{
    /// <summary>Validates the item asynchronously with caller data.</summary>
    ValueTask<bool> ValidateAsync(TContext context, CancellationToken cancellationToken = default);
}
