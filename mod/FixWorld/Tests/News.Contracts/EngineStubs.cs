using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public int DestroyCalls;
        public static void Destroy(Object value) { if (value != null) ++value.DestroyCalls; }
    }
    public enum TextureFormat { Alpha8 }
    public enum FilterMode { Bilinear }
    public class Texture2D : Object
    {
        public static readonly List<Texture2D> Created = new();
        private byte mode;
        public string name;
        public FilterMode filterMode;
        public int anisoLevel;
        public Texture2D(int width, int height, TextureFormat format, bool mipmaps) { Created.Add(this); }
        public bool LoadImage(byte[] bytes) { mode = bytes[0]; return mode != 0; }
        public void Compress(bool highQuality) { if (mode == 2) throw new InvalidOperationException("Synthetic compression failure"); }
        public void Apply(bool mipmaps, bool unreadable) { }
    }
}
namespace Verse
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class StaticConstructorOnStartupAttribute : Attribute { }
    public sealed class ModContentPack
    {
        public string RootDir;
        public string PackageIdPlayerFacing;
    }
    public static class BaseContent { public const string BadTexPath = "BadTexture"; }
    public static class ContentFinder<T> where T : class
    {
        public static readonly Dictionary<string, T> Assets = new();
        public static T Get(string path, bool reportFailure = true) => Assets.TryGetValue(path, out var value) ? value : null;
    }
}
namespace FixWorld
{
    public static class FixWorldController { public static readonly TestLogger Logger = new(); }
    public sealed class TestLogger { public void Warning(string message) { } }
}
namespace FixWorld.News
{
    public static class UpdateFeatureManager { public const string UpdateFeatureDefFolder = "News"; }
}
