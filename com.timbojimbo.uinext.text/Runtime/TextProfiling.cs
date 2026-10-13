using Unity.Profiling;

namespace TimboJimbo.UINext.Text
{
    /// <summary>
    /// The markers UI Next Text shows in the Unity Profiler: reading markup (UINext.Text.Parse), laying text out
    /// (UINext.Text.Generate) and measuring it (UINext.Text.Measure, UINext.Text.LongestWord). They cost next to nothing
    /// while the Profiler isn't recording.
    /// </summary>
    internal static class TextProfiling
    {
        // UI Next's own category, which its markers are in too.
        private static readonly ProfilerCategory Category = new("UI Next");

        public static readonly ProfilerMarker Parse = new(Category, "UINext.Text.Parse");
        public static readonly ProfilerMarker Generate = new(Category, "UINext.Text.Generate");
        public static readonly ProfilerMarker Measure = new(Category, "UINext.Text.Measure");
        public static readonly ProfilerMarker LongestWord = new(Category, "UINext.Text.LongestWord");
    }
}
