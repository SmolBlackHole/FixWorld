using System;
using System.Linq;
using System.Xml;
using Verse;

namespace FixWorld.Loading
{
    internal static class PatchLoadingPipeline
    {
        internal static void Check()
        {
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                PatchOperation[] patches = LoadPatches(mod);
                if (patches.Length == 0)
                {
                    continue;
                }

                LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                    LoadingStep.CheckPatches,
                    "Check patches",
                    "Checking patches for " + mod.Name,
                    mod));
                try
                {
                    for (int index = 0; index < patches.Length; index++)
                    {
                        PatchOperation patch = patches[index];
                        try
                        {
                            foreach (string error in patch.ConfigErrors())
                            {
                                Log.Error(
                                    "Config error in " + mod.Name + " patch " +
                                    patch + ": " + error);
                            }
                        }
                        catch (Exception exception)
                        {
                            Log.Error(
                                "Exception in ConfigErrors() of " + mod.Name +
                                " patch " + patch + ": " + exception);
                        }

                        operation.ReportProgress(
                            index + 1,
                            patches.Length,
                            "Checking patches for " + mod.Name);
                    }
                }
                catch
                {
                    operation.Fail();
                    throw;
                }
                finally
                {
                    operation.Dispose();
                }
            }
        }

        internal static void Apply(XmlDocument xmlDocument)
        {
            foreach (ModContentPack mod in LoadedModManager.RunningModsListForReading)
            {
                PatchOperation[] patches = mod.Patches.ToArray();
                if (patches.Length == 0)
                {
                    continue;
                }

                LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                    LoadingStep.ApplyPatches,
                    "Apply patches",
                    "Applying patches from " + mod.Name,
                    mod));
                try
                {
                    for (int index = 0; index < patches.Length; index++)
                    {
                        try
                        {
                            patches[index].Apply(xmlDocument);
                        }
                        catch (Exception exception)
                        {
                            Log.Error("Error in patch.Apply(): " + exception);
                        }

                        operation.ReportProgress(
                            index + 1,
                            patches.Length,
                            "Applying patches from " + mod.Name);
                    }
                }
                catch
                {
                    operation.Fail();
                    throw;
                }
                finally
                {
                    operation.Dispose();
                }
            }
        }

        internal static LoadingOperation BeginOriginal(
            LoadingStep step,
            string displayName,
            string activity)
        {
            return LoadingEvents.Begin(new LoadingStageEventDescriptor(
                LoadingStage.XmlAndPatches,
                step,
                displayName,
                activity,
                "RimWorld",
                LoadingModAttribution.Global));
        }

        private static PatchOperation[] LoadPatches(ModContentPack mod)
        {
            LoadingOperation operation = LoadingEvents.Begin(Descriptor(
                LoadingStep.LoadPatchFiles,
                "Load patch files",
                "Loading patch files for " + mod.Name,
                mod));
            try
            {
                PatchOperation[] patches = mod.Patches.ToArray();
                operation.ReportProgress(
                    patches.Length,
                    patches.Length,
                    "Loaded patch files for " + mod.Name);
                return patches;
            }
            catch
            {
                operation.Fail();
                throw;
            }
            finally
            {
                operation.Dispose();
            }
        }

        private static LoadingStageEventDescriptor Descriptor(
            LoadingStep step,
            string displayName,
            string activity,
            ModContentPack mod)
        {
            return new LoadingStageEventDescriptor(
                LoadingStage.XmlAndPatches,
                step,
                displayName,
                activity,
                mod.Name,
                LoadingModAttribution.Exact(mod));
        }
    }
}
