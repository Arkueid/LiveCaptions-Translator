using System.Text.RegularExpressions;

namespace LiveCaptionsTranslator.utils
{
    public static partial class RegexPatterns
    {
        // Remove the `.` between two uppercase letters. (Cope with acronym)
        [GeneratedRegex(@"([A-Z])\s*\.\s*([A-Z])(?![A-Za-z]+)")]
        public static partial Regex Acronym();

        // If an acronym is followed by a word, preserve the space between them.
        [GeneratedRegex(@"([A-Z])\s*\.\s*([A-Z])(?=[A-Za-z]+)")]
        public static partial Regex AcronymWithWords();

        // Remove redundant spaces and `\n` around punctuation.
        [GeneratedRegex(@"\s*([.!?,])\s*")]
        public static partial Regex PunctuationSpace();

        // If it is Chinese or Japanese punctuation, no need to insert spaces.
        [GeneratedRegex(@"\s*([。！？，、])\s*")]
        public static partial Regex CJPunctuationSpace();

        // `[...]` notice prefix (monospace latency tag, `[ERROR]`, `[WARNING]`, `[Paused]`).
        // The bracket part is restricted to a single line without inner brackets, so it can never
        // swallow a chunk of the translation; the body matches anything, including the newlines a
        // model sometimes emits (without `[\s\S]` the whole match fails and the body comes back
        // empty, which used to make the overlay lose its translation line).
        [GeneratedRegex(@"^(\[[^\]\r\n]+\] )?([\s\S]*)$")]
        public static partial Regex NoticePrefixAndTranslation();

        [GeneratedRegex(@"^\[[^\]\r\n]+\] ")]
        public static partial Regex NoticePrefix();

        [GeneratedRegex(@"^(https?:\/\/)")]
        public static partial Regex HttpPrefix();

        [GeneratedRegex(@"\/{2,}")]
        public static partial Regex MultipleSlashes();

        [GeneratedRegex(@"<think>.*?<\/think>")]
        public static partial Regex ModelThinking();

        [GeneratedRegex(@"[^0-9.]")]
        public static partial Regex VersionNumber();

        [GeneratedRegex(@"🔤\s*(.+?)\s*🔤")]
        public static partial Regex TargetSentence();
    }
}
