namespace LiveCaptionsTranslator.Utils
{
    public enum FontBold
    {
        None = 0,
        TranslationOnly = 1,
        SubtitleOnly = 2,
        Both = 3
    }

    public enum CaptionVisible
    {
        Both = 0,
        TranslationOnly = 1,
        SubtitleOnly = 2
    }

    public enum CaptionLocation
    {
        TranslationTop = 0,
        SubtitleTop = 1
    }

    public enum Color
    {
        White = 1,
        Yellow = 2,
        LimeGreen = 3,
        Aqua = 4,
        Blue = 5,
        DeepPink = 6,
        Red = 7,
        Black = 8
    }

    public sealed class CaptionFontPreset
    {
        public string DisplayName { get; }

        // A comma separated fallback list: WPF picks the first family that is actually installed,
        // and falls back per glyph for characters a family does not cover. An empty value means
        // "inherit the translation font" (only meaningful for the source line).
        public string Family { get; }

        public CaptionFontPreset(string displayName, string family)
        {
            DisplayName = displayName;
            Family = family;
        }

        public override string ToString() => DisplayName;
    }

    public static class CaptionFontPresets
    {
        // Slightly rounder than the default system UI font, but still a normal-width sans-serif,
        // so subtitles stay readable at a glance.
        public const string DefaultFamily = "Noto Sans SC, Microsoft YaHei UI, Segoe UI";

        // Japanese sources need Japanese glyph forms: Chinese fonts draw kana inconsistently and
        // render shared kanji with Chinese shapes (直 / 骨 / 真 and friends), which reads as
        // "almost right" and is worse than an obvious mismatch.
        public const string JapaneseNoto = "Noto Sans JP, メイリオ, Yu Gothic UI, Microsoft YaHei UI";
        public const string JapaneseMeiryo = "Meiryo UI, メイリオ, Yu Gothic UI, Noto Sans JP, Microsoft YaHei UI";
        public const string JapaneseYuGothic = "Yu Gothic UI, Yu Gothic, メイリオ, Noto Sans JP, Microsoft YaHei UI";

        // Translation (and fallback source) fonts. These are picked for Chinese output.
        public static IReadOnlyList<CaptionFontPreset> Translation { get; } = new[]
        {
            new CaptionFontPreset("Noto Sans SC（圆润）", DefaultFamily),
            new CaptionFontPreset("幼圆（最圆）", "幼圆, YouYuan, Microsoft YaHei UI, Segoe UI"),
            new CaptionFontPreset("等线（轻盈）", "DengXian, Microsoft YaHei UI, Segoe UI"),
            new CaptionFontPreset("微软雅黑（系统默认）", "Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
            new CaptionFontPreset("Segoe UI（拉丁优先）", "Segoe UI, Microsoft YaHei UI"),
        };

        // Recognised (original) line. Independent from the translation font, because the audio
        // language and the target language are usually different scripts.
        public static IReadOnlyList<CaptionFontPreset> Source { get; } = new[]
        {
            new CaptionFontPreset("跟随译文字体", string.Empty),
            new CaptionFontPreset("日语 · メイリオ（易读）", JapaneseMeiryo),
            new CaptionFontPreset("日语 · Noto Sans JP（现代）", JapaneseNoto),
            new CaptionFontPreset("日语 · Yu Gothic UI（清爽）", JapaneseYuGothic),
            new CaptionFontPreset("中文 · Noto Sans SC", DefaultFamily),
            new CaptionFontPreset("中文 · 微软雅黑", "Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
            new CaptionFontPreset("拉丁 · Segoe UI", "Segoe UI, Microsoft YaHei UI"),
        };
    }

    public static class StyleConsts
    {
        public const int MAX_FONT_SIZE = 40;
        public const int MIN_FONT_SIZE = 8;
        public const int DELTA_FONT_SIZE = 1;

        public const int MAX_OPACITY = 251;
        public const int MIN_OPACITY = 1;
        public const int DELTA_OPACITY = 25;

        public const double MAX_STROKE = 7.5;
        public const double MIN_STROKE = 0.0;
        public const double DELTA_STROKE = 1.5;

        public const int DELTA_OVERLAY_HEIGHT = 40;
    }
}
