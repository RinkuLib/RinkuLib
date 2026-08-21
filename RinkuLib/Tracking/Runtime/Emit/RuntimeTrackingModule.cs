using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeTrackingModule
{
    private static int _id;
    private static readonly AssemblyBuilder Assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Rinku.RuntimeTracking"), AssemblyBuilderAccess.Run);
    internal static readonly ModuleBuilder Module = Assembly.DefineDynamicModule("Rinku.RuntimeTracking");
    internal static int NextId() => Interlocked.Increment(ref _id);
}
