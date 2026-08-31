using System.Runtime.CompilerServices;

namespace FixWorld
{
    internal static class ModuleBootstrap
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            FixWorldBootstrap.InitializeRuntime();
        }
    }
}
