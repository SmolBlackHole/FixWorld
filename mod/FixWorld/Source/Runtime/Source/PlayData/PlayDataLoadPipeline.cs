using System;
using FixWorld.Preloader;

namespace FixWorld.PlayData
{
    internal sealed class PlayDataLoadPipeline
    {
        private readonly Action beginLoad;
        private readonly Action completeLoad;
        private readonly Action<Exception> failLoad;
        private readonly DeferredWorkQueue deferredWork;
        private readonly ModLoadingPipeline modLoading;
        private readonly RimWorldPlayData rimWorld;
        private readonly PlayDataStageRunner stages;

        internal PlayDataLoadPipeline(
            PlayDataStageRunner stages,
            ModLoadingPipeline modLoading,
            RimWorldPlayData rimWorld,
            DeferredWorkQueue deferredWork,
            Action beginLoad,
            Action completeLoad,
            Action<Exception> failLoad)
        {
            this.stages = stages ?? throw new ArgumentNullException(nameof(stages));
            this.modLoading = modLoading ??
                throw new ArgumentNullException(nameof(modLoading));
            this.rimWorld = rimWorld ?? throw new ArgumentNullException(nameof(rimWorld));
            this.deferredWork = deferredWork ??
                throw new ArgumentNullException(nameof(deferredWork));
            this.beginLoad = beginLoad ??
                throw new ArgumentNullException(nameof(beginLoad));
            this.completeLoad = completeLoad ??
                throw new ArgumentNullException(nameof(completeLoad));
            this.failLoad = failLoad ?? throw new ArgumentNullException(nameof(failLoad));
        }

        internal void Load()
        {
            beginLoad();
            deferredWork.BeginCapture();
            try
            {
                PreloaderTimelineContract.PublishRuntimeOwnsModBoot();
                stages.Run(PlayDataLoadStage.Reset, () =>
                {
                    rimWorld.Reset();
                    modLoading.Reset();
                });
                stages.Run(
                    PlayDataLoadStage.InitializeMods,
                    modLoading.InitializeMods);
                stages.Run(
                    PlayDataLoadStage.IndexModContent,
                    modLoading.IndexContent);
                stages.Run(
                    PlayDataLoadStage.PrepareTextureCache,
                    modLoading.PrepareTextureCache);
                stages.Run(
                    PlayDataLoadStage.PrepareModContent,
                    modLoading.PrepareContent);
                stages.Run(
                    PlayDataLoadStage.CreateModClasses,
                    modLoading.CreateModClasses);
                ModXmlState xml = stages.Run(
                    PlayDataLoadStage.LoadAndPatchXml,
                    modLoading.LoadAndPatchXml);
                stages.Run(PlayDataLoadStage.ImportDefinitions, () =>
                {
                    modLoading.ImportDefinitions(xml);
                    rimWorld.ImportDefinitions();
                });
                stages.Run(
                    PlayDataLoadStage.EarlyBinding,
                    rimWorld.RunEarlyBinding);
                stages.Run(
                    PlayDataLoadStage.PreResolveImpliedDefinitions,
                    rimWorld.GeneratePreResolveDefinitions);
                stages.Run(
                    PlayDataLoadStage.CrossReferenceResolution,
                    rimWorld.ResolveCrossReferences);
                stages.Run(
                    PlayDataLoadStage.ReferenceResolution,
                    rimWorld.ResolveDefinitions);
                stages.Run(
                    PlayDataLoadStage.PostResolveImpliedDefinitions,
                    rimWorld.GeneratePostResolveDefinitions);
                stages.Run(
                    PlayDataLoadStage.DefinitionFinalization,
                    rimWorld.FinalizeDefinitions);
                stages.Run(
                    PlayDataLoadStage.InitializeRuntime,
                    rimWorld.InitializeRuntime);
                deferredWork.Schedule(stages, completeLoad, failLoad);
            }
            catch (Exception exception)
            {
                deferredWork.Abort();
                failLoad(exception);
                throw;
            }
        }
    }
}
