namespace Rinku;

/// <summary>
/// The base class for errors reported by Rinku.
/// Use <see cref="Code"/> when application code must identify a specific error.
/// </summary>
/// <remarks>
/// See <see cref="ErrorCodes"/> for the available codes.
/// </remarks>
public abstract class RinkuException : Exception {
    /// <summary>The <c>RINKU####</c> identifier for the condition that failed.</summary>
    public string Code { get; }

    /// <summary>Creates a Rinku error with a stable code.</summary>
    /// <param name="code">One of the constants on <see cref="ErrorCodes"/>.</param>
    /// <param name="message">What failed, naming the specific thing that failed.</param>
    /// <param name="innerException">The original error when one is available.</param>
    protected RinkuException(string code, string message, Exception? innerException = null)
        : base($"{code}: {message}", innerException) {
        Code = code;
        HelpLink = DocsAnchor + code.ToLowerInvariant();
    }

    internal const string DocsAnchor = "https://rinkulib.github.io/RinkuLib/articles/reference/errors.html#";
}

/// <summary>A template could not be read, see the <c>RINKU1###</c> band.</summary>
public class RinkuTemplateException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>A command could not be prepared from a template and its values, see the <c>RINKU2###</c> band.</summary>
public class RinkuBindingException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>No parser could be built for the target type, see the <c>RINKU3###</c> band.</summary>
public class RinkuMappingException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>A result could not be read through its parser, see the <c>RINKU4###</c> band.</summary>
public class RinkuReadException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>A type's configuration was refused, see the <c>RINKU5###</c> band.</summary>
public class RinkuConfigurationException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>A tracked value could not be copied or edited, see the <c>RINKU6###</c> band.</summary>
public class RinkuTrackingException(string code, string message, Exception? innerException = null)
    : RinkuException(code, message, innerException);

/// <summary>
/// An invariant inside the library did not hold. Reaching one of these is a bug in RinkuLib rather than a
/// mistake in the calling code, see the <c>RINKU9###</c> band.
/// </summary>
public class RinkuInternalException(string code, string message, Exception? innerException = null)
    : RinkuException(code, $"{message}. This is a bug in RinkuLib, please report it with the stack trace.", innerException);
