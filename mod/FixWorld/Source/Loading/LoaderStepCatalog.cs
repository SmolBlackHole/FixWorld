using System;

namespace FixWorld.Loading
{
    internal readonly struct StepDescriptor
    {
        internal readonly LoadingStep Step;
        internal readonly LoadingStage Stage;
        internal readonly string Name;
        internal readonly string DisplayName;
        internal readonly string ModName;
        internal readonly string ModActivity;

        internal StepDescriptor(
            LoadingStep step,
            LoadingStage stage,
            string name,
            string displayName = null,
            string modName = null,
            string modActivity = null)
        {
            Step = step;
            Stage = stage;
            Name = name;
            DisplayName = displayName ?? name;
            ModName = modName;
            ModActivity = modActivity;
        }
    }

    internal static class LoaderStepCatalog
    {
        private const string TexturePrefix = "Loading assets of type UnityEngine.Texture2D for mod ";
        private const string AudioPrefix = "Loading assets of type UnityEngine.AudioClip for mod ";
        private const string StringPrefix = "Loading assets of type System.String for mod ";

        internal static bool TryMatch(string label, out StepDescriptor descriptor)
        {
            switch (label)
            {
                case "LoadModXML()":
                    descriptor = Step(LoadingStep.LoadXml, LoadingStage.XmlAndPatches, "Load XML");
                    return true;
                case "CombineIntoUnifiedXML()":
                    descriptor = Step(LoadingStep.CombineXml, LoadingStage.XmlAndPatches, "Combine XML");
                    return true;
                case "TKeySystem.Parse()":
                    descriptor = Step(LoadingStep.ParseTranslationKeys, LoadingStage.XmlAndPatches, "Parse translation keys");
                    return true;
                case "ErrorCheckPatches()":
                    descriptor = Step(LoadingStep.CheckPatches, LoadingStage.XmlAndPatches, "Check patches");
                    return true;
                case "ApplyPatches()":
                    descriptor = Step(LoadingStep.ApplyPatches, LoadingStage.XmlAndPatches, "Apply patches");
                    return true;
                case "ParseAndProcessXML()":
                    descriptor = Step(LoadingStep.ParseDefinitions, LoadingStage.XmlAndPatches, "Parse definitions");
                    return true;
                case "ClearCachedPatches()":
                    descriptor = Step(LoadingStep.ClearPatchCache, LoadingStage.XmlAndPatches, "Clear patch cache");
                    return true;
                case "Load language metadata.":
                    descriptor = Step(LoadingStep.LoadLanguageMetadata, LoadingStage.Definitions, "Load language metadata");
                    return true;
                case "Copy all Defs from mods to global databases.":
                    descriptor = Step(LoadingStep.CopyDefinitions, LoadingStage.Definitions, "Copy definitions");
                    return true;
                case "TKeySystem.BuildMappings()":
                    descriptor = Step(LoadingStep.BuildLanguageMappings, LoadingStage.Definitions, "Build language mappings");
                    return true;
                case "Resolve references.":
                    descriptor = Step(LoadingStep.ResolveDefinitions, LoadingStage.Definitions, "Resolve definitions");
                    return true;
                case "Load keyboard preferences.":
                    descriptor = Step(
                        LoadingStep.LoadKeyboardPreferences,
                        LoadingStage.Definitions,
                        "Loading keyboard preferences");
                    return true;
                case "Short hash giving.":
                    descriptor = Step(
                        LoadingStep.AssignDefinitionIds,
                        LoadingStage.Definitions,
                        "Assigning definition IDs");
                    return true;
                case "ExecuteToExecuteWhenFinished()":
                    descriptor = new StepDescriptor(
                        LoadingStep.DelayedInitialization,
                        LoadingStage.Content,
                        "Delayed initialization",
                        "Executing delayed initialization tasks");
                    return true;
                case "LoadModContent":
                    descriptor = Step(LoadingStep.LoadContent, LoadingStage.Content, "Load mod content");
                    return true;
                case "Reload audio clips":
                    descriptor = Step(LoadingStep.LoadAudio, LoadingStage.Content, "Load audio");
                    return true;
                case "Reload textures":
                    descriptor = Step(LoadingStep.LoadTextures, LoadingStage.Content, "Load textures");
                    return true;
                case "Reload strings":
                    descriptor = Step(LoadingStep.LoadStrings, LoadingStage.Content, "Load strings");
                    return true;
                case "Reload asset bundles":
                    descriptor = Step(LoadingStep.LoadAssetBundles, LoadingStage.Content, "Load asset bundles");
                    return true;
                case "Load all bios":
                    descriptor = Step(LoadingStep.LoadBios, LoadingStage.Finalize, "Load bios");
                    return true;
                case "Inject selected language data into game data.":
                    descriptor = Step(LoadingStep.InjectLanguage, LoadingStage.Finalize, "Inject language data");
                    return true;
                case "Static constructor calls":
                case "StaticConstructorOnStartupUtility.CallAll()":
                    descriptor = Step(LoadingStep.RunStaticConstructors, LoadingStage.Finalize, "Calling static constructors");
                    return true;
                case "Finalize static initialization":
                    descriptor = Step(
                        LoadingStep.FinalizeStaticInitialization,
                        LoadingStage.Finalize,
                        "Finalize mod frameworks");
                    return true;
                case "Check static constructor attributes":
                    descriptor = Step(
                        LoadingStep.CheckStaticConstructorAttributes,
                        LoadingStage.Finalize,
                        "Check startup attributes");
                    return true;
                case "Atlas baking.":
                    descriptor = Step(LoadingStep.BakeAtlases, LoadingStage.Finalize, "Bake atlases");
                    return true;
                case "Garbage Collection":
                    descriptor = Step(LoadingStep.GarbageCollection, LoadingStage.Finalize, "Clean up");
                    return true;
            }

            if (StartsWith(label, "Resolve cross-references"))
            {
                descriptor = Step(LoadingStep.ResolveCrossReferences, LoadingStage.Definitions, "Resolve cross-references");
                return true;
            }

            if (StartsWith(label, "Rebind DefOfs"))
            {
                descriptor = Step(LoadingStep.RebindDefinitions, LoadingStage.Definitions, "Rebind definitions");
                return true;
            }

            if (StartsWith(label, "Generate implied Defs"))
            {
                descriptor = Step(LoadingStep.GenerateImpliedDefinitions, LoadingStage.Definitions, "Generate implied definitions");
                return true;
            }

            if (StartsWith(label, "Other def binding"))
            {
                descriptor = Step(LoadingStep.ResolveDefinitions, LoadingStage.Definitions, "Bind definitions");
                return true;
            }

            if (TryMatchMod(
                    label,
                    TexturePrefix,
                    LoadingStep.LoadTextures,
                    "Load textures",
                    "Textures",
                    out descriptor) ||
                TryMatchMod(
                    label,
                    AudioPrefix,
                    LoadingStep.LoadAudio,
                    "Load audio",
                    "Audio",
                    out descriptor) ||
                TryMatchMod(
                    label,
                    StringPrefix,
                    LoadingStep.LoadStrings,
                    "Load strings",
                    "Strings",
                    out descriptor))
            {
                return true;
            }

            if (TryMatchModContent(label, out descriptor))
            {
                return true;
            }

            descriptor = default;
            return false;
        }

        internal static string GetDisplayName(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return "Working";
            }

            const string separator = " -> ";
            int separatorIndex = label.IndexOf(separator, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return label;
            }

            string typeName = GetSimpleTypeName(label.Substring(0, separatorIndex).Trim());
            string methodName = GetMethodName(
                label.Substring(separatorIndex + separator.Length).Trim());
            return string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName)
                ? label
                : typeName + "." + methodName;
        }

        private static bool TryMatchMod(
            string label,
            string prefix,
            LoadingStep step,
            string name,
            string activity,
            out StepDescriptor descriptor)
        {
            if (!StartsWith(label, prefix))
            {
                descriptor = default;
                return false;
            }

            string modName = label.Substring(prefix.Length);
            descriptor = new StepDescriptor(
                step,
                LoadingStage.Content,
                name,
                name,
                modName,
                activity);
            return true;
        }

        private static bool TryMatchModContent(string label, out StepDescriptor descriptor)
        {
            const string prefix = "Loading ";
            const string suffix = " content";
            if (!StartsWith(label, prefix) ||
                !label.EndsWith(suffix, StringComparison.Ordinal) ||
                label.Length <= prefix.Length + suffix.Length)
            {
                descriptor = default;
                return false;
            }

            string modName = label.Substring(
                prefix.Length,
                label.Length - prefix.Length - suffix.Length);
            descriptor = new StepDescriptor(
                LoadingStep.LoadContent,
                LoadingStage.Content,
                "Load mod content",
                "Load mod content",
                modName,
                "Mod content");
            return true;
        }

        private static bool StartsWith(string value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal);
        }

        private static string GetSimpleTypeName(string typeName)
        {
            int nestedTypeIndex = typeName.IndexOf('+');
            if (nestedTypeIndex >= 0)
            {
                typeName = typeName.Substring(0, nestedTypeIndex);
            }

            int namespaceIndex = typeName.LastIndexOf('.');
            if (namespaceIndex >= 0)
            {
                typeName = typeName.Substring(namespaceIndex + 1);
            }

            int genericIndex = typeName.IndexOf('`');
            return genericIndex >= 0 ? typeName.Substring(0, genericIndex) : typeName;
        }

        private static string GetMethodName(string signature)
        {
            int generatedNameStart = signature.IndexOf('<');
            int generatedNameEnd = generatedNameStart >= 0
                ? signature.IndexOf('>', generatedNameStart + 1)
                : -1;
            if (generatedNameStart >= 0 && generatedNameEnd > generatedNameStart + 1)
            {
                return signature.Substring(
                    generatedNameStart + 1,
                    generatedNameEnd - generatedNameStart - 1);
            }

            int parameterIndex = signature.IndexOf('(');
            string name = parameterIndex >= 0
                ? signature.Substring(0, parameterIndex).Trim()
                : signature;
            int returnTypeIndex = name.LastIndexOf(' ');
            return returnTypeIndex >= 0 ? name.Substring(returnTypeIndex + 1) : name;
        }

        private static StepDescriptor Step(LoadingStep step, LoadingStage stage, string name)
        {
            return new StepDescriptor(step, stage, name);
        }
    }
}
