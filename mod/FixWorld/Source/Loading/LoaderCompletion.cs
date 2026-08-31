using FixWorld.Caching;
using FixWorld.Diagnostics;
using FixWorld.Preloader;
using Verse;

namespace FixWorld.Loading
{
    internal static class LoaderCompletion
    {
        internal static void Complete(string source)
        {
            TextureDdsCache.Complete();
            LoadingStageMailbox.Drain();
            if (!LoadingSession.TryComplete())
            {
                return;
            }

            LoadingTelemetry.Complete();
            BenchmarkRecorder.Complete(source);
            Log.Message("[FixWorld] Main menu ready.");
            TextureDdsCache.StartDeferredBuild();
            PreloaderPrompt.TryShow();
        }
    }
}
