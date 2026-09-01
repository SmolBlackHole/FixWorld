using System;
using Verse;

namespace FixWorld.Loading
{
    internal static class ModBootStageRunner
    {
        internal static void Run(
            LoadingStageEventDescriptor descriptor,
            Action execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            RunCore(
                descriptor,
                operation =>
                {
                    execute();
                    return false;
                });
        }

        internal static TOutput Run<TOutput>(
            LoadingStageEventDescriptor descriptor,
            Func<TOutput> execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            return RunCore(descriptor, _ => execute());
        }

        internal static TOutput Run<TOutput>(
            LoadingStageEventDescriptor descriptor,
            Func<LoadingOperation, TOutput> execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            return RunCore(descriptor, execute);
        }

        private static TOutput RunCore<TOutput>(
            LoadingStageEventDescriptor descriptor,
            Func<LoadingOperation, TOutput> execute)
        {
            if (execute == null)
            {
                throw new ArgumentNullException(nameof(execute));
            }

            LongEventHandler.SetCurrentEventText(
                "FixWorld: " + descriptor.DisplayName);
            LoadingOperation operation = LoadingEvents.Begin(descriptor);
            try
            {
                return execute(operation);
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
}
