using System;
using System.Collections.Generic;
using System.IO;
using FixWorld.Scheduling;
using Verse;

namespace FixWorld.Textures
{
    internal sealed class TextureCacheAdapter
    {
        private readonly JobScheduler scheduler;
        private readonly MainThreadQueue mainThread;

        internal TextureCacheAdapter(
            JobScheduler scheduler,
            MainThreadQueue mainThread)
        {
            this.scheduler = scheduler ??
                throw new ArgumentNullException(nameof(scheduler));
            this.mainThread = mainThread ??
                throw new ArgumentNullException(nameof(mainThread));
        }

        internal void Attach(string modRoot, float maximumGiB)
        {
            TextureDdsCache.Initialize(
                modRoot,
                maximumGiB,
                scheduler,
                mainThread);
        }

        internal void Apply(
            ModContentPack mod,
            string contentPath,
            List<string> foldersToLoadDebug,
            Dictionary<string, FileInfo> files)
        {
            TextureDdsCache.Apply(
                mod,
                contentPath,
                foldersToLoadDebug,
                files);
        }

        internal void Complete()
        {
            TextureDdsCache.Complete();
        }

        internal void StartDeferredBuild()
        {
            TextureDdsCache.StartDeferredBuild();
        }

        internal TextureDdsCacheSnapshot Snapshot()
        {
            return TextureDdsCache.GetSnapshot();
        }

        internal void Shutdown()
        {
            TextureDdsCache.Shutdown();
        }
    }
}
