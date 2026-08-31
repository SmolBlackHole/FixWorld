using FixWorld.Diagnostics;
using FixWorld.Preloader;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoaderCompletion
    {
        internal static void Complete(string source)
        {
            if (!LoadingSession.TryComplete())
            {
                return;
            }

            BenchmarkRecorder.Complete(source);
            Log.Message("[FixWorld] Main menu ready.");
            PreloaderPrompt.TryShow();
        }
    }
}
