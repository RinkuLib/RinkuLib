namespace Rinku.Tracking.Core;

internal static class DefaultConstructor<T>
{
    internal static readonly Func<T>? Factory = CreateFactory();

    private static Func<T>? CreateFactory()
    {
        Type type = typeof(T);
        if (type.IsAbstract || type.IsInterface || !type.IsValueType && type.GetConstructor(Type.EmptyTypes) is null) return null;
        return static () => Activator.CreateInstance<T>();
    }
}
