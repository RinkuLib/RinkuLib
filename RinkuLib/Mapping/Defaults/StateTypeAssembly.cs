using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
#if NET9_0_OR_GREATER
using GateLock = System.Threading.Lock;
#else
using GateLock = System.Object;
#endif

namespace Rinku.Mapping.Defaults;

internal static class StateTypeAssembly {
    private static readonly AssemblyBuilder Assembly;
    private static readonly ModuleBuilder Module;
    private static readonly ConstructorInfo IgnoresCtor =
        TypeExtensions.Require(typeof(IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)]), nameof(IgnoresCtor));
    private static readonly HashSet<string> Allowed = [];
    private static readonly GateLock Gate = new();

    static StateTypeAssembly() {
        Assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Rinku.MultiRowStates"), AssemblyBuilderAccess.Run);
        Module = Assembly.DefineDynamicModule("Main");
        AllowAccessTo(typeof(StateTypeAssembly).Assembly);
    }

    internal static void AllowAccessTo(Assembly assembly) {
        var name = assembly.GetName().Name;
        if (name is null)
            return;
        lock (Gate) {
            if (Allowed.Add(name))
                Assembly.SetCustomAttribute(new CustomAttributeBuilder(IgnoresCtor, [name]));
        }
    }

    internal static void AllowAccessTo(Type type) {
        AllowAccessTo(type.Assembly);
        if (type.HasElementType) {
            AllowAccessTo(type.GetElementType() ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"Element type missing for {type}."));
            return;
        }
        if (!type.IsGenericType)
            return;
        foreach (var argument in type.GetGenericArguments())
            AllowAccessTo(argument);
    }

    internal static TypeBuilder DefineState(string name) =>
        Module.DefineType(
            name,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout | TypeAttributes.BeforeFieldInit,
            typeof(ValueType));
}
