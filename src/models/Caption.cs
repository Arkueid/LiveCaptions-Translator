using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    public class Caption : INotifyPropertyChanged
    {
        public const int MAX_CONTEXTS = 10;

        private static Caption? instance = null;
        public event PropertyChangedEventHandler? PropertyChanged;

        public Queue<TranslationHistoryEntry> Contexts { get; } = new(MAX_CONTEXTS);

        private CaptionSnapshot snapshot = CaptionSnapshot.Empty;

        /// <summary>
        /// Everything the overlay draws, as one immutable object. The display used to be assembled
        /// from half a dozen properties (live source, live translation, the source that translation
        /// belonged to, the notice prefix, the list of finished pairs) that were written one after
        /// another from different threads — every intermediate state was a picture the overlay could
        /// paint, which is how sentence groups ended up duplicated, reordered or missing.
        /// </summary>
        public CaptionSnapshot Snapshot
        {
            get => snapshot;
            private set
            {
                snapshot = value;
                OnPropertyChanged(nameof(Snapshot));
            }
        }

        public void Publish(CaptionSnapshot next)
        {
            if (next.HasSameContent(snapshot))
                return;
            Snapshot = next;
        }

        private Caption()
        {
        }

        public static Caption GetInstance()
        {
            if (instance != null)
                return instance;
            instance = new Caption();
            return instance;
        }

        public IEnumerable<TranslationHistoryEntry> AwareContexts => GetPreviousContexts(Translator.Setting.NumContexts);
        public string AwareContextsCaption => GetPreviousText(Translator.Setting.NumContexts, TextType.Caption);

        public string GetPreviousText(int count, TextType textType)
        {
            if (count <= 0 || Contexts.Count == 0)
                return string.Empty;

            var prev = Contexts
                .Reverse().Take(count).Reverse()
                .Select(entry => entry == null || string.CompareOrdinal(entry.TranslatedText, "N/A") == 0 ||
                                 entry.TranslatedText.Contains("[ERROR]") || entry.TranslatedText.Contains("[WARNING]") ?
                    "" : (textType == TextType.Caption ? entry.SourceText : entry.TranslatedText))
                .Aggregate((accu, cur) =>
                {
                    if (!string.IsNullOrEmpty(accu))
                    {
                        if (Array.IndexOf(TextUtil.PUNC_EOS, accu[^1]) == -1)
                            accu += TextUtil.isCJChar(accu[^1]) ? "。" : ". ";
                        else
                            accu += TextUtil.isCJChar(accu[^1]) ? "" : " ";
                    }
                    cur = RegexPatterns.NoticePrefix().Replace(cur, "");
                    return accu + cur;
                });

            if (textType == TextType.Translation)
                prev = RegexPatterns.NoticePrefix().Replace(prev, "");
            if (!string.IsNullOrEmpty(prev) && Array.IndexOf(TextUtil.PUNC_EOS, prev[^1]) == -1)
                prev += TextUtil.isCJChar(prev[^1]) ? "。" : ".";
            if (!string.IsNullOrEmpty(prev) && Encoding.UTF8.GetByteCount(prev[^1].ToString()) < 2)
                prev += " ";
            return prev;
        }

        public IEnumerable<TranslationHistoryEntry> GetPreviousContexts(int count)
        {
            if (count <= 0 || Contexts.Count == 0)
                return [];

            return Contexts
                .Reverse()
                .Where(entry => entry != null && string.CompareOrdinal(entry.TranslatedText, "N/A") != 0 &&
                                !entry.TranslatedText.Contains("[ERROR]") &&
                                !entry.TranslatedText.Contains("[WARNING]"))
                .Take(count)
                .Reverse();
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public enum TextType
    {
        Caption,
        Translation
    }
}
