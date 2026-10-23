using System.Text;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// Turns the stream of caption hypotheses LiveCaptions pushes into a *monotonic* transcript.
    ///
    /// LiveCaptions never appends: the automation `Name` is the whole caption box, and the
    /// recognizer keeps rewriting the words around its caret while the speaker talks. The previous
    /// pipeline re-derived the visible line from that box on every update — find the text of the
    /// last committed segment in the buffer, split there, and fall back to "after the last full
    /// stop" when the search failed. A rewrite near that point therefore moved the split backwards
    /// or forwards, and the overlay visibly took characters back and printed them again a moment
    /// later.
    ///
    /// Here text only moves in one direction: <c>[ committed ][ tail ]</c>. <see cref="Update"/>
    /// decides how much of the new hypothesis is stable — the prefix that also appeared in the
    /// previous hypothesis, minus a small hold-back window — and appends exactly that. Whatever is
    /// still being revised stays in <see cref="Tail"/> and is thrown away next round. Committed text
    /// is never edited, so the display can never un-print a character it has already shown.
    /// </summary>
    public sealed class TranscriptStabilizer
    {
        // Display cells at the end of a hypothesis that may still change. A CJK glyph is two cells,
        // so this holds back roughly two characters: the window the recognizer actually rewrites.
        private const int HOLD_BACK_UNITS = 4;

        // Once the hypothesis stops changing, nothing else can prove its tail stable — commit it
        // after this long so the last word of a sentence is not left invisible when the speaker
        // pauses.
        private const int QUIET_FLUSH_MS = 220;

        // Minimum head of the buffer that must coincide with the end of the committed text before
        // the two are considered the same run of speech rather than a fresh one.
        private const int MIN_OVERLAP = 3;

        // The buffer is compared against at most this much of the committed text when looking for
        // the overlapping suffix. Long enough for a couple of caption lines, bounded for cost.
        private const int MAX_OVERLAP_SCAN = 160;

        // How far back from the last boundary a revision is allowed to shift it. A recognizer edits
        // a few characters around its caret; anything further is treated as new text.
        private const int REWIND_WINDOW = 12;

        // Length of the end of the committed text used to place the buffer when nothing else works.
        private const int ANCHOR_LENGTH = 12;
        private const int MIN_ANCHOR = 4;

        private readonly StringBuilder committed = new();
        private string tail = string.Empty;
        private string buffer = string.Empty;
        private int tailStart;
        private long lastUpdateTicks;

        /// <summary>Everything that has been decided. Append-only.</summary>
        public string Committed => committed.ToString();

        /// <summary>The part of the newest hypothesis that is still allowed to change.</summary>
        public string Tail => tail;

        public void Reset()
        {
            committed.Clear();
            tail = string.Empty;
            buffer = string.Empty;
            tailStart = 0;
            lastUpdateTicks = Environment.TickCount64;
        }

        /// <summary>
        /// Feeds one hypothesis (the whole caption box, already normalized) and returns the text
        /// that just became stable — possibly empty.
        /// </summary>
        public string Update(string hypothesis, long now)
        {
            if (string.IsNullOrEmpty(hypothesis))
                return string.Empty;
            if (string.CompareOrdinal(hypothesis, buffer) == 0)
                return string.Empty;

            string previousTail = tail;
            var (boundary, rewound) = Boundary(hypothesis, previousTail);
            string candidate = hypothesis.Substring(boundary);
            // How much of the new tail is the same as the tail that was on probation last round.
            int echoed = rewound ? 0 : CommonPrefix(candidate, previousTail);

            int commitChars = StableLength(candidate, echoed);
            string chunk = string.Empty;
            if (commitChars > 0)
            {
                chunk = candidate.Substring(0, commitChars);
                committed.Append(chunk);
            }

            tail = candidate.Substring(commitChars);
            tailStart = hypothesis.Length - tail.Length;
            buffer = hypothesis;
            lastUpdateTicks = now;
            return chunk;
        }

        /// <summary>
        /// Commits the whole tail once the hypothesis has been quiet for a moment: no further
        /// update is coming that could revise it.
        /// </summary>
        public string FlushQuiet(long now)
        {
            if (tail.Length == 0 || now - lastUpdateTicks < QUIET_FLUSH_MS)
                return string.Empty;

            string chunk = tail;
            committed.Append(chunk);
            tail = string.Empty;
            // The tail now starts at the end of the buffer; without this the next update would take
            // the flushed characters for undecided text and commit them a second time.
            tailStart = buffer.Length;
            return chunk;
        }

        /// <summary>
        /// Number of leading characters of <paramref name="candidate"/> that are stable: the ones
        /// that were already there last round (up to <paramref name="echoed"/>), minus the hold-back
        /// window measured in display cells.
        /// </summary>
        private static int StableLength(string candidate, int echoed)
        {
            int stable = Math.Min(echoed, candidate.Length);
            if (stable == 0)
                return 0;

            int units = 0;
            for (int i = 0; i < stable; i++)
                units += TextUtil.CharUnits(candidate[i]);

            int limit = units - HOLD_BACK_UNITS;
            if (limit <= 0)
                return 0;

            int consumed = 0;
            int chars = 0;
            while (chars < stable)
            {
                consumed += TextUtil.CharUnits(candidate[chars]);
                if (consumed > limit)
                    break;
                chars++;
            }
            return chars;
        }

        /// <summary>
        /// Index in <paramref name="hypothesis"/> where the text that is still undecided begins.
        /// <para>
        /// Three situations have to be told apart and they look alike: the buffer only grew (or its
        /// tail was rewritten) — the decided text is untouched, so the boundary is where it was; the
        /// recognizer revised a character or two just before the boundary, which shifts everything
        /// after it; and the box scrolled, so the head of the buffer sits inside the decided text.
        /// </para>
        /// The previous tail is the only evidence about where the buffer starts, so every candidate
        /// offset is scored by how well the text it implies continues that tail. Ties keep the offset
        /// we already had: nothing is ever re-emitted on a coin flip.
        /// </summary>
        private (int Offset, bool Rewound) Boundary(string hypothesis, string previousTail)
        {
            if (buffer.Length == 0)
                return (0, false);

            int prefix = CommonPrefix(hypothesis, buffer);
            int keepTail = Math.Min(tailStart, hypothesis.Length);

            // The decided text is identical: only the tail moved, there is nothing to guess.
            if (prefix >= tailStart)
                return (keepTail, false);

            int keepScore = CommonPrefix(hypothesis.AsSpan(keepTail), previousTail);

            // The box scrolled: the head of the buffer is still inside the previous one, so the
            // boundary moves with it. Mapping through the previous buffer keeps the answer
            // unambiguous even when a phrase repeats.
            int window = SuffixPrefixOverlap(buffer, hypothesis);
            if (window >= MIN_OVERLAP)
            {
                int shifted = keepTail - (buffer.Length - window);
                if (shifted >= 0 && shifted < keepTail &&
                    CommonPrefix(hypothesis.AsSpan(shifted), previousTail) >= keepScore)
                    return (shifted, false);
            }

            // A revision sits before the boundary and shifted everything after it.
            if (prefix >= MIN_OVERLAP)
                return ReviseBuffer(hypothesis, previousTail, prefix, keepTail);

            // Heads have nothing in common and no shift was found: place the buffer by the end of
            // what was committed, which is the only thing known to have been said.
            int anchored = CommittedAnchor(hypothesis);
            return anchored > 0 ? (anchored, false) : (keepTail, false);
        }

        /// <summary>
        /// The buffer still agrees with the previous one at the head, so the difference is a revision.
        /// Look for the offset that continues the tail best, and never allow the revision to be
        /// committed on the spot: rewinding means the buffer holds a revised version of text that was
        /// committed already, and printing it again is exactly the duplicate the timeline must not get.
        /// </summary>
        private (int Offset, bool Rewound) ReviseBuffer(
            string hypothesis, string previousTail, int prefix, int keepTail)
        {
            int best = keepTail;
            int bestScore = CommonPrefix(hypothesis.AsSpan(keepTail), previousTail);

            int from = Math.Max(prefix, keepTail - REWIND_WINDOW);
            for (int offset = keepTail - 1; offset >= from; offset--)
            {
                int score = CommonPrefix(hypothesis.AsSpan(offset), previousTail);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = offset;
                }
            }

            return (best, best < keepTail);
        }

        /// <summary>
        /// Places the buffer by the end of the committed text: the tail of what we know was said has to
        /// appear in it somewhere. This is the last resort, used when the box scrolled and the previous
        /// buffer was itself revised at the end, which breaks the head-to-head comparison.
        /// </summary>
        private int CommittedAnchor(string hypothesis)
        {
            int length = Math.Min(ANCHOR_LENGTH, committed.Length);
            for (; length >= MIN_ANCHOR; length -= 4)
            {
                // Only the end of the builder is needed; materialising the whole transcript here
                // would copy hundreds of kilobytes on a long session.
                string anchor = committed.ToString(committed.Length - length, length);
                int index = hypothesis.LastIndexOf(anchor, StringComparison.Ordinal);
                if (index >= 0)
                    return index + length;
            }
            return -1;
        }

        /// <summary>
        /// Length of the longest suffix of <paramref name="text"/> that is also a prefix of
        /// <paramref name="other"/>.
        /// </summary>
        private static int SuffixPrefixOverlap(string text, string other)
        {
            int max = Math.Min(Math.Min(text.Length, other.Length), MAX_OVERLAP_SCAN);
            for (int length = max; length >= MIN_OVERLAP; length--)
            {
                if (other.AsSpan(0, length).SequenceEqual(
                        text.AsSpan(text.Length - length, length)))
                    return length;
            }
            return 0;
        }

        private static int CommonPrefix(ReadOnlySpan<char> first, string second) =>
            CommonPrefix(first, second.AsSpan());

        private static int CommonPrefix(ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        {
            int max = Math.Min(first.Length, second.Length);
            int i = 0;
            while (i < max && first[i] == second[i])
                i++;
            return i;
        }
    }
}
