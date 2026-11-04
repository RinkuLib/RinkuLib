using System.Text;

namespace Rinku.More.Globalization;

/// <summary>
/// Validation helpers for composite format strings loaded at runtime.
/// </summary>
public static class StringFormatExtensions
{
    private const int StackallocThreshold = 256;

    public static int GetNbFormatParams(this string format, int maxCount = 10)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

        Span<bool> paramsFound = maxCount <= StackallocThreshold
            ? stackalloc bool[maxCount]
            : new bool[maxCount];
        return format.GetNbFormatParams(paramsFound);
    }

    public static bool CheckValidFormat(this string format, int maxCount = 10, bool perfectMatch = false)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

        Span<bool> paramsFound = maxCount <= StackallocThreshold
            ? stackalloc bool[maxCount]
            : new bool[maxCount];
        return format.CheckValidFormat(paramsFound, perfectMatch);
    }

    public static bool CheckValidFormat(this string format, Span<bool> paramsFound, bool perfectMatch) =>
        format.GetNbFormatParams(paramsFound) is var count &&
        count >= 0 &&
        (!perfectMatch || count == paramsFound.Length);

    public static int GetNbFormatParams(this string format, Span<bool> paramsFound)
    {
        if (!format.CheckAndFillSpan(paramsFound))
            return -1;

        var count = 0;
        for (var i = 0; i < paramsFound.Length; i++)
        {
            if (paramsFound[i])
                count++;
        }

        return count;
    }

    public static bool CheckAndFillSpan(this string format, Span<bool> paramsFound)
    {
        ArgumentNullException.ThrowIfNull(format);
        paramsFound.Clear();

        try
        {
            var requiredArguments = CompositeFormat.Parse(format).MinimumArgumentCount;
            if (requiredArguments > paramsFound.Length)
                return false;

            paramsFound[..requiredArguments].Fill(true);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
