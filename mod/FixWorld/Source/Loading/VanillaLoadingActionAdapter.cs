using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace FixWorld.Loading
{
    internal static class VanillaLoadingActionAdapter
    {
        private static readonly Dictionary<MethodInfo, AdapterKind> Kinds =
            new Dictionary<MethodInfo, AdapterKind>();

        internal static LoadingActionPlan CreatePlan(Action action, string label)
        {
            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                AdapterKind kind = GetKind(action.Method);
                if (kind == AdapterKind.Finalization &&
                    FinalizationPipeline.TryCreateCompatible(
                        action,
                        out FinalizationPipeline finalization))
                {
                    return finalization.CreatePlan(action, label);
                }

                if (kind == AdapterKind.Content &&
                    ContentLoadingPipeline.TryCreateCompatible(
                        action,
                        out ContentLoadingPipeline content))
                {
                    return content.CreatePlan(label);
                }

                return LoadingActionPlan.CreateFallback(action, label);
            }
            finally
            {
                LoadingTelemetry.ObserveOverhead(
                    LoadingOverheadKind.Classification,
                    Stopwatch.GetTimestamp() - startedAt);
            }
        }

        private static AdapterKind GetKind(MethodInfo method)
        {
            if (Kinds.TryGetValue(method, out AdapterKind kind))
            {
                return kind;
            }

            kind = Classify(method);
            Kinds.Add(method, kind);
            return kind;
        }

        private static AdapterKind Classify(MethodInfo method)
        {
            if (ContentLoadingPipeline.MatchesContract(method))
            {
                return AdapterKind.Content;
            }

            if (FinalizationPipeline.IsCandidate(method) &&
                FinalizationPipeline.MatchesContract(method))
            {
                return AdapterKind.Finalization;
            }

            return AdapterKind.Regular;
        }

        private enum AdapterKind
        {
            Regular,
            Content,
            Finalization
        }
    }
}
