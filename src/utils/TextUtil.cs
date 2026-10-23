using System.Text;

namespace LiveCaptionsTranslator.utils
{
    public static class TextUtil
    {
        public static readonly char[] PUNC_EOS = ".?!。？！".ToCharArray();
        public static readonly char[] PUNC_COMMA = ",，、—\n".ToCharArray();
        private static readonly char[] PUNC_ALL = PUNC_EOS.Concat(PUNC_COMMA).ToArray();

        // Breaks a sentence can be cut at when it has grown too long and the speaker has not
        // produced a full stop yet. A clause break is far less destructive than a hard cut.
        private static readonly char[] PUNC_CLAUSE = "、，,".ToCharArray();

        public const int SHORT_THRESHOLD = 10;
        public const int MEDIUM_THRESHOLD = 40;
        public const int LONG_THRESHOLD = 160;
        public const int VERYLONG_THRESHOLD = 220;

        // A subtitle segment is measured in display cells: a CJK character takes two, a Latin one
        // one. 100 cells is what two lines of the overlay can show, so longer segments are cut.
        public const int MAX_SEGMENT_UNITS = 100;

        public const double SIM_THRESHOLD = 0.6;

        // When the exact split anchor is gone, shorter and shorter heads are tried before the buffer
        // is treated as entirely new.
        private const int ANCHOR_RETRY_MIN = 16;
        private const int ANCHOR_RETRY_STEP = 8;

        public static string ShortenDisplaySentence(string text, int maxByteLength)
        {
            while (Encoding.UTF8.GetByteCount(text) >= maxByteLength)
            {
                int puncIndex = text.IndexOfAny(PUNC_ALL);
                if (puncIndex < 0 || puncIndex + 1 >= text.Length)
                    break;
                text = text.Substring(puncIndex + 1);
            }
            return text;
        }

        /// <summary>
        /// LiveCaptions wraps Japanese captions wherever the box ends — often in the middle of a
        /// word — and each line break is only a display wrap, not a sentence end. The old code
        /// appended a 「。」 (long line) or 「——」 (short line) to every break, which invented a full
        /// stop every ~13 characters: 87% of the sentences handed to the translator were fake
        /// fragments of a sentence the speaker had not finished. Lines are now joined back into
        /// continuous text, and sentence ends come from real punctuation only.
        /// </summary>
        public static string JoinCaptionLines(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0)
                return text;

            var builder = new StringBuilder(text.Length);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                // Latin script needs the whitespace the line break was standing in for; CJK does not.
                if (builder.Length > 0 && !isCJChar(builder[^1]) && !isCJChar(line[0]))
                    builder.Append(' ');

                builder.Append(line);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Width of `text` in display cells: CJK/fullwidth characters count as two, everything
        /// else as one. Byte counts overstate Latin text and understate kana, which is why the
        /// old thresholds behaved so differently between languages.
        /// </summary>
        public static int DisplayUnits(string text)
        {
            int units = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    units += 2;
                    i++;
                    continue;
                }
                units += CharUnits(ch);
            }
            return units;
        }

        /// <summary>Display cells taken by a single UTF-16 code unit.</summary>
        public static int CharUnits(char ch) => IsWideChar(ch) ? 2 : 1;

        private static bool IsWideChar(char ch) =>
            isCJChar(ch) ||
            (ch >= '\uFF00' && ch <= '\uFF60') ||   // Fullwidth forms
            (ch >= '\uFFE0' && ch <= '\uFFE6');

        /// <summary>
        /// Returns the index just past a good cut point when `text` is longer than `maxUnits`
        /// display cells, or -1 when it fits. The cut prefers the last clause break (「、」 etc.)
        /// so a sentence is split where a speaker would pause; without one it falls back to a hard
        /// cut at the limit, never inside a surrogate pair.
        /// </summary>
        public static int FindClauseCut(string text, int maxUnits)
        {
            if (string.IsNullOrEmpty(text) || DisplayUnits(text) <= maxUnits)
                return -1;

            int units = 0;
            int lastClause = -1;
            int hardCut = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    units += 2;
                    if (hardCut < 0 && units > maxUnits)
                        hardCut = i;
                    i++;
                    continue;
                }

                units += IsWideChar(ch) ? 2 : 1;
                if (Array.IndexOf(PUNC_CLAUSE, ch) != -1)
                    lastClause = i;
                if (hardCut < 0 && units > maxUnits)
                    hardCut = i;
            }

            // Only trust a clause break that is not right at the start (it would make a stub).
            if (lastClause > 0 && DisplayUnits(text.Substring(0, lastClause + 1)) >= maxUnits / 2)
                return lastClause + 1;

            if (hardCut <= 0)
                return -1;

            // Never split a Latin word in half: back off to the space before the limit.
            if (!IsWideChar(text[hardCut]))
            {
                int space = text.LastIndexOf(' ', hardCut);
                if (space > 0 && hardCut - space <= 16)
                    return space + 1;
            }

            return hardCut;
        }

        /// <summary>
        /// Splits the caption buffer into finished segments plus the sentence still being spoken.
        /// <para>
        /// `anchor` is the tail of the last segment already handled: everything after its last
        /// occurrence in `fullText` is new. Anchoring on text rather than on an index keeps the
        /// split stable while LiveCaptions rewrites the words around its caret.
        /// </para>
        /// A segment ends at a real full stop, or at the last clause break once it has grown past
        /// `maxUnits` display cells. Nothing here invents punctuation.
        /// </summary>
        public static (List<string> Completed, string Current, string Anchor) SplitCaption(
            string fullText, string anchor, int maxUnits, int anchorLength)
        {
            var completed = new List<string>();
            if (string.IsNullOrEmpty(fullText))
                return (completed, string.Empty, string.Empty);

            // Where the new text starts: right after the last segment we already handled.
            int start = 0;
            if (!string.IsNullOrEmpty(anchor))
            {
                int matched = anchor.Length;
                int found = fullText.LastIndexOf(anchor, StringComparison.Ordinal);
                // The recognizer keeps refining the words at its caret, so the tail of the anchor can
                // change while its head still matches. Retry with a shorter head: whatever follows
                // it is then simply part of the line being spoken again — harmless — whereas giving
                // up restarts at the top of the buffer and re-translates the whole finished
                // sentence, which shows up on screen as the same group printed twice. The tail is
                // never shortened: a short tail matches all sorts of repeated phrases later in the
                // buffer and would skip the words in between.
                while (found < 0 && matched > ANCHOR_RETRY_MIN)
                {
                    matched = Math.Max(ANCHOR_RETRY_MIN, matched - ANCHOR_RETRY_STEP);
                    found = fullText.LastIndexOf(anchor.Substring(0, matched), StringComparison.Ordinal);
                }

                start = found >= 0
                    ? found + matched
                    // Buffer was rewritten out from under us (LiveCaptions restarted or revised
                    // earlier text). Resume after the last full stop instead of replaying it all.
                    : Math.Max(0, fullText.LastIndexOfAny(PUNC_EOS) + 1);
            }

            // 1) Every real full stop closes a segment.
            int eos;
            while (start < fullText.Length && (eos = fullText.IndexOfAny(PUNC_EOS, start)) >= 0)
            {
                string segment = fullText.Substring(start, eos - start + 1).Trim();
                if (segment.Length > 0)
                    completed.Add(segment);
                start = eos + 1;
            }

            // 2) A run of speech without a full stop is cut at its last clause break once it has
            //    grown past what the overlay can show.
            string raw = start < fullText.Length ? fullText.Substring(start) : string.Empty;
            string current = raw.TrimStart();
            int currentStart = start + (raw.Length - current.Length);

            int cut = FindClauseCut(current, maxUnits);
            if (cut > 0)
            {
                string head = current.Substring(0, cut).TrimEnd();
                if (head.Length > 0)
                    completed.Add(head);
                currentStart += cut;
                current = fullText.Substring(currentStart).TrimStart();
            }

            // 3) The anchor is the tail of everything handled so far.
            if (currentStart <= 0)
                return (completed, current, string.Empty);

            int anchorStart = Math.Max(0, currentStart - anchorLength);
            string newAnchor = fullText.Substring(anchorStart, currentStart - anchorStart).TrimEnd();
            return (completed, current, newAnchor);
        }


        public static bool HasContent(string text)
        {
            foreach (char ch in text)
                if (char.IsLetterOrDigit(ch))
                    return true;
            return false;
        }

        /// <summary>
        /// Terms the speech recognizer reliably mishears in this domain, mapped to the word the
        /// speaker actually used. スパチャ/赤スパ ("super chat" / "red super chat") in particular is
        /// heard as スパッツ or split into スーパー + スパ, and then translated as clothing or vegetables.
        /// One pass, so a replacement can never feed the next pattern.
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex SpokenTermRegex = new(
            @"赤スーパー|赤スパ|スーパーチャット|スーパーチャ|スパチャ|スパッツ",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string NormalizeSpokenTerms(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return SpokenTermRegex.Replace(text, match =>
                match.Value.StartsWith("赤", StringComparison.Ordinal)
                    ? "赤スーパーチャット"
                    : "スーパーチャット");
        }

        public static bool isCJChar(char ch)
        {
            return
                (ch >= '\u4E00' && ch <= '\u9FFF') ||   // CJ Unified Ideographs
                (ch >= '\u3400' && ch <= '\u4DBF') ||   // CJ Unified Ideographs Extension A
                (ch >= '\u3000' && ch <= '\u303F') ||   // CJ Symbols and Punctuation
                (ch >= '\u3040' && ch <= '\u309F') ||   // Hiragana
                (ch >= '\u30A0' && ch <= '\u30FF') ||   // Katakana
                (ch >= '\u31F0' && ch <= '\u31FF') ||   // Katakana Phonetic Extensions
                (ch >= '\u3200' && ch <= '\u32FF') ||   // Enclosed CJ Letters and Months
                (ch >= '\u3300' && ch <= '\u33FF');     // CJ Unit Symbols
        }

        public static double Similarity(string text1, string text2)
        {
            if (text1.StartsWith(text2) || text2.StartsWith(text1))
                return 1.0;
            int distance = LevenshteinDistance(text1, text2);
            int maxLen = Math.Max(text1.Length, text2.Length);
            return (maxLen == 0) ? 1.0 : (1.0 - (double)distance / maxLen);
        }

        public static int LevenshteinDistance(string text1, string text2)
        {
            if (string.IsNullOrEmpty(text1))
                return string.IsNullOrEmpty(text2) ? 0 : text2.Length;
            if (string.IsNullOrEmpty(text2))
                return text1.Length;

            if (text1.Length > text2.Length)
                (text2, text1) = (text1, text2);

            int len1 = text1.Length;
            int len2 = text2.Length;

            int[] previous = new int[len1 + 1];
            int[] current = new int[len1 + 1];

            for (int i = 0; i <= len1; i++)
                previous[i] = i;
            for (int j = 1; j <= len2; j++)
            {
                current[0] = j;
                for (int i = 1; i <= len1; i++)
                {
                    int cost = (text1[i - 1] == text2[j - 1]) ? 0 : 1;
                    current[i] = Math.Min(
                        Math.Min(current[i - 1] + 1, previous[i] + 1),
                        previous[i - 1] + cost);
                }
                (current, previous) = (previous, current);
            }

            return previous[len1];
        }

        public static string NormalizeUrl(string url)
        {
            var protocolMatch = RegexPatterns.HttpPrefix().Match(url);
            string protocol = protocolMatch.Success ? protocolMatch.Value : "";

            string rest = url.Substring(protocol.Length);
            rest = RegexPatterns.MultipleSlashes().Replace(rest, "/");
            rest = rest.TrimEnd('/');

            return protocol + rest;
        }
    }
}
