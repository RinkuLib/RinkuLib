using System.Runtime.CompilerServices;
using Rinku.Querying.Parameters;

namespace Rinku.Querying.Defaults;

/// <summary>Registers the query defaults supplied by Rinku.</summary>
public static class DefaultQueryBootstrap {
    private static int Initialized;

    /// <summary>Installs the shipped query defaults. Calling this more than once has no effect.</summary>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize() {
        if (Interlocked.Exchange(ref Initialized, 1) != 0)
            return;
        DbParameterDefaults.TryInstall(new DefaultDbParameterServices());
        QueryFactory.BaseHandlerMapper.ResetWith(('S', StringVariableHandler.Build), ('R', RawVariableHandler.Build), ('N', NumberVariableHandler.Build));
        SpecialHandler.SpecialHandlerGetter['X'] = MultiVariableHandler.Build;
    }
}
