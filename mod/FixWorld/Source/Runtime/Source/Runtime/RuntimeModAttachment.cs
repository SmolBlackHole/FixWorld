using System;
using Verse;

namespace FixWorld.Runtime
{
    internal sealed class RuntimeModAttachmentSnapshot
    {
        private RuntimeModAttachmentSnapshot(
            Mod mod,
            ModContentPack content,
            RuntimeModSettingsSnapshot settings)
        {
            Mod = mod;
            Content = content;
            Settings = settings;
        }

        internal Mod Mod { get; }

        internal ModContentPack Content { get; }

        internal RuntimeModSettingsSnapshot Settings { get; }

        internal static RuntimeModAttachmentSnapshot Create(
            object mod,
            object content,
            float ddsCacheMaxGiB)
        {
            Mod typedMod = mod as Mod ??
                throw new ArgumentException(
                    "The FixWorld mod instance is invalid.",
                    nameof(mod));
            ModContentPack typedContent = content as ModContentPack ??
                throw new ArgumentException(
                    "The FixWorld content pack is invalid.",
                    nameof(content));
            if (string.IsNullOrWhiteSpace(typedContent.RootDir))
            {
                throw new ArgumentException(
                    "The FixWorld content root is required.",
                    nameof(content));
            }

            RequireAssemblyCount("FixWorld.Runtime", 1);
            RequireAssemblyCount("FixWorld.Mod", 1);
            RequireAssemblyCount("FixWorld", 0);
            if (!string.Equals(
                    typedMod.GetType().Assembly.GetName().Name,
                    "FixWorld.Mod",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The normal FixWorld mod must come from FixWorld.Mod.dll.");
            }

            return new RuntimeModAttachmentSnapshot(
                typedMod,
                typedContent,
                new RuntimeModSettingsSnapshot(ddsCacheMaxGiB));
        }

        private static void RequireAssemblyCount(
            string assemblyName,
            int expected)
        {
            int count = 0;
            foreach (System.Reflection.Assembly assembly in
                     AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(
                        assembly.GetName().Name,
                        assemblyName,
                        StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count != expected)
            {
                throw new InvalidOperationException(
                    "Expected " + expected + " loaded " + assemblyName +
                    " assembly, found " + count + ".");
            }
        }
    }

    internal sealed class RuntimeModSettingsSnapshot
    {
        internal RuntimeModSettingsSnapshot(float ddsCacheMaxGiB)
        {
            if (float.IsNaN(ddsCacheMaxGiB) ||
                float.IsInfinity(ddsCacheMaxGiB) ||
                ddsCacheMaxGiB < 1.0f ||
                ddsCacheMaxGiB > 64.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ddsCacheMaxGiB),
                    "The DDS cache limit must be between 1 and 64 GiB.");
            }

            DdsCacheMaxGiB = ddsCacheMaxGiB;
        }

        internal float DdsCacheMaxGiB { get; }
    }
}
