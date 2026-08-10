using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Rinku.Mapping.Defaults;

/// <summary>
/// The single dynamic assembly every multi-row state struct is emitted into. It carries an
/// <see cref="IgnoresAccessChecksToAttribute"/> for each assembly whose non-public members an emitted method
/// must reach, so a method on a state type reads private setters and calls private constructors directly, the
/// way <see cref="DynamicMethod"/> does through <c>skipVisibility</c>.
/// </summary>
internal static class StateTypeAssembly {
    private static readonly AssemblyBuilder Assembly;
    private static readonly ModuleBuilder Module;
    private static readonly ConstructorInfo IgnoresCtor =
        typeof(IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])!;
    private static readonly HashSet<string> Allowed = [];
    private static readonly object Gate = new();

    static StateTypeAssembly() {
        Assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Rinku.MultiRowStates"), AssemblyBuilderAccess.Run);
        Module = Assembly.DefineDynamicModule("Main");
        AllowAccessTo(typeof(StateTypeAssembly).Assembly);
    }

    /// <summary>
    /// Grants every emitted method access to the non-public members of <paramref name="assembly"/>. Idempotent
    /// and thread-safe, so it is called for each assembly a state type touches before that type is built.
    /// </summary>
    internal static void AllowAccessTo(Assembly assembly) {
        var name = assembly.GetName().Name;
        if (name is null)
            return;
        lock (Gate) {
            if (Allowed.Add(name))
                Assembly.SetCustomAttribute(new CustomAttributeBuilder(IgnoresCtor, [name]));
        }
    }

    /// <summary>Grants access for a constructed type and every type captured by its shape.</summary>
    internal static void AllowAccessTo(Type type) {
        AllowAccessTo(type.Assembly);
        if (type.HasElementType) {
            AllowAccessTo(type.GetElementType()!);
            return;
        }
        if (!type.IsGenericType)
            return;
        foreach (var argument in type.GetGenericArguments())
            AllowAccessTo(argument);
    }

    /// <summary>
    /// Defines a new state struct (a <see cref="ValueType"/>) under the given unique name. The caller adds the
    /// fields, constructor, and methods, then calls <see cref="TypeBuilder.CreateType"/>.
    /// </summary>
    internal static TypeBuilder DefineState(string name) =>
        Module.DefineType(
            name,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit,
            typeof(ValueType));
}
