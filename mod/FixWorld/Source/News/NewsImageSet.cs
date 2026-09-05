// SPDX-License-Identifier: MPL-2.0
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FixWorld.News
{
    internal readonly struct NewsImage
    {
        internal NewsImage(Texture2D texture, bool owned) { Texture = texture; Owned = owned; }
        internal Texture2D Texture { get; }
        internal bool Owned { get; }
    }

    // Window-owned Unity resources, not a generic derived-data cache.
    // Replace, Reset and the queued callback execute on the UI/main thread.
    internal sealed class NewsImageSet
    {
        private readonly Dictionary<(ModContentPack mod, string name), NewsImage> images = new();
        private long generation;
        internal bool Pending { get; private set; }

        internal void Replace(IEnumerable<(ModContentPack mod, string name)> requests, Action<Action> enqueue)
        {
            Reset();
            var required = requests.Distinct().ToArray();
            if (required.Length == 0) return;
            var requestedGeneration = generation;
            Pending = true;
            try
            {
                enqueue(() =>
                {
                    if (requestedGeneration != generation) return;
                    try
                    {
                        foreach (var key in required)
                            images.Add(key, UpdateFeatureImageLoader.GetImage(key.mod, key.name));
                    }
                    catch
                    {
                        Reset();
                        throw;
                    }
                    finally { Pending = false; }
                });
            }
            catch { Reset(); throw; }
        }

        internal bool TryGet(ModContentPack mod, string name, out Texture2D texture)
        {
            var found = images.TryGetValue((mod, name), out var image);
            texture = image.Texture;
            return found;
        }

        internal void Reset()
        {
            ++generation;
            Pending = false;
            foreach (var image in images.Values)
                if (image.Owned) UnityEngine.Object.Destroy(image.Texture);
            images.Clear();
        }
    }
}
