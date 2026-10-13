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
        public static readonly ProfilerMarker Parse = new(ProfilerCategory.Gui, "UINext.Text.Parse");
        public static readonly ProfilerMarker Generate = new(ProfilerCategory.Gui, "UINext.Text.Generate");
        public static readonly ProfilerMarker Measure = new(ProfilerCategory.Gui, "UINext.Text.Measure");
        public static readonly ProfilerMarker LongestWord = new(ProfilerCategory.Gui, "UINext.Text.LongestWord");
    }
}
