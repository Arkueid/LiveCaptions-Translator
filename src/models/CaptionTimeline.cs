using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    /// <summary>One line of the overlay: a source sentence and the translation that belongs to it.</summary>
    public sealed class CaptionLine
    {
        public int Seq { get; init; }
        public string Source { get; init; } = string.Empty;
        public string Translation { get; init; } = string.Empty;
        /// <summary>Notice printed in front of the translation, e.g. the latency tag or `[ERROR]`.</summary>
        public string Notice { get; init; } = string.Empty;
        /// <summary>True for the sentence the speaker has not finished yet.</summary>
        public bool IsLive { get; init; }
        public bool IsFinal { get; init; }
    }

    /// <summary>
    /// Immutable description of what the overlay shows. Publishing one object instead of a handful
    /// of properties is what removes the "half updated" states the old display code had to guess
    /// around (and got wrong: duplicated and swallowed sentence groups).
    /// </summary>
    public sealed class CaptionSnapshot
    {
        public static readonly CaptionSnapshot Empty = new();

        public IReadOnlyList<CaptionLine> Lines { get; init; } = Array.Empty<CaptionLine>();
        /// <summary>`[Paused]` and friends, drawn on the newest line.</summary>
        public string LiveNotice { get; init; } = string.Empty;
        public int Revision { get; init; }

        /// <summary>Cheap structural comparison, so an unchanged snapshot never repaints.</summary>
        public bool HasSameContent(CaptionSnapshot? other)
        {
            if (other == null || other.Lines.Count != Lines.Count ||
                !string.Equals(other.LiveNotice, LiveNotice, StringComparison.Ordinal))
                return false;

            for (int i = 0; i < Lines.Count; i++)
            {
                var a = Lines[i];
                var b = other.Lines[i];
                if (a.Seq != b.Seq || a.IsLive != b.IsLive ||
                    !string.Equals(a.Source, b.Source, StringComparison.Ordinal) ||
                    !string.Equals(a.Translation, b.Translation, StringComparison.Ordinal) ||
                    !string.Equals(a.Notice, b.Notice, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
    }

    public enum CaptionTranslationState
    {
        None,
        Pending,
        Ready
    }

    /// <summary>What has to be translated. The sequence number is the identity of the sentence.</summary>
    public sealed class TranslationRequest
    {
        public TranslationRequest(int seq, string text, bool isFinal)
        {
            Seq = seq;
            Text = text;
            IsFinal = isFinal;
        }

        public int Seq { get; }
        public string Text { get; }
        public bool IsFinal { get; }
    }

    /// <summary>
    /// The transcript as an ordered, append-only list of sentences.
    ///
    /// Every finished sentence gets a sequence number when it is created and keeps it forever, so
    /// "which sentence does this translation belong to", "has this already been logged" and "what
    /// comes next on screen" are answered by an integer instead of by comparing strings. The old
    /// code had no such identity: it guessed with a Levenshtein threshold whether two texts were
    /// the same sentence, and every guess it got wrong either swallowed a sentence group or printed
    /// one twice.
    ///
    /// Segments are only ever appended or extended while they are still open; a closed segment is
    /// immutable. The overlay simply draws the last N of them.
    /// </summary>
    public sealed class CaptionTimeline
    {
        // History is kept in memory for the context window and for the display; only the tail is
        // ever drawn, so this is just a bound on memory.
        private const int MAX_SEGMENTS = 60;

        private readonly object gate = new();
        private readonly List<CaptionSegment> segments = new();
        private int nextSeq = 1;
        private int revision;

        public void Reset()
        {
            lock (gate)
            {
                segments.Clear();
                nextSeq = 1;
                revision++;
            }
        }

        /// <summary>
        /// Appends text that has just been stabilized. It is split into sentences at a real full
        /// stop, or at the last clause break once a run gets longer than the overlay can show.
        /// </summary>
        public void Append(string text, long now)
        {
            if (string.IsNullOrEmpty(text))
                return;

            lock (gate)
            {
                var open = GetOrCreateOpen();
                string pending = open.Source + text;
                open.Source = string.Empty;

                while (pending.Length > 0)
                {
                    int eos = pending.IndexOfAny(TextUtil.PUNC_EOS);
                    int end = eos >= 0
                        ? eos + 1
                        : TextUtil.FindClauseCut(pending, TextUtil.MAX_SEGMENT_UNITS);
                    if (end <= 0)
                        break;

                    string head = pending.Substring(0, end).Trim();
                    pending = pending.Substring(end).TrimStart();

                    // A lone 「。」 between two sentences is not a sentence.
                    if (head.Length == 0 || !TextUtil.HasContent(head))
                        continue;

                    open.Source = head;
                    Close(open, now);

                    if (pending.Length == 0)
                    {
                        open = null;
                        break;
                    }
                    open = NewSegment();
                }

                if (open != null)
                {
                    open.Source = pending;
                    open.LastChangeTicks = now;
                }

                Trim();
                revision++;
            }
        }

        /// <summary>
        /// True when a finished sentence is waiting to be translated. Read-only — unlike
        /// <see cref="NextFinalRequest"/> it does not claim the sentence, so it is safe to call from
        /// the caption thread just to decide whether to interrupt a speculative request.
        /// </summary>
        public bool HasPendingFinal()
        {
            lock (gate)
            {
                foreach (var segment in segments)
                {
                    if (segment.IsFinal && !segment.Logged &&
                        segment.State == CaptionTranslationState.None && segment.Source.Length > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Milliseconds since the open segment last grew; 0 when there is none.</summary>
        public long OpenIdleMs(long now)
        {
            lock (gate)
            {
                if (segments.Count == 0 || segments[^1].IsFinal)
                    return 0;
                return now - segments[^1].LastChangeTicks;
            }
        }

        /// <summary>Marks the open segment as finished (the speaker stopped, or the session ended).</summary>
        public bool FinalizeOpen(long now)
        {
            lock (gate)
            {
                if (segments.Count == 0 || segments[^1].IsFinal)
                    return false;
                var open = segments[^1];
                if (open.Source.Length == 0 || !TextUtil.HasContent(open.Source))
                    return false;

                Close(open, now);
                revision++;
                return true;
            }
        }

        /// <summary>
        /// The next finished sentence that still needs a translation, oldest first — finished
        /// sentences are what the reader actually reads, so they always go before the live one.
        /// </summary>
        public TranslationRequest? NextFinalRequest()
        {
            lock (gate)
            {
                foreach (var segment in segments)
                {
                    if (!segment.IsFinal || segment.Logged ||
                        segment.State != CaptionTranslationState.None ||
                        segment.Source.Length == 0)
                        continue;

                    segment.State = CaptionTranslationState.Pending;
                    return new TranslationRequest(segment.Seq, segment.Source, true);
                }
                return null;
            }
        }

        /// <summary>
        /// A request for the sentence still being spoken, following the same debounce/force rules as
        /// before: translate it once it stops growing, or once the line has gone stale.
        /// </summary>
        public TranslationRequest? NextLiveRequest(int debounceMs, int forceMs, long now)
        {
            lock (gate)
            {
                if (segments.Count == 0)
                    return null;

                var open = segments[^1];
                if (open.IsFinal || open.Source.Length == 0)
                    return null;
                if (string.CompareOrdinal(open.Source, open.LastRequestText) == 0)
                    return null;

                bool quiet = now - open.LastChangeTicks >= debounceMs;
                bool stale = open.LastRequestTicks == 0 || now - open.LastRequestTicks >= forceMs;
                if (!quiet && !stale)
                    return null;

                open.LastRequestText = open.Source;
                open.LastRequestTicks = now;
                open.State = CaptionTranslationState.Pending;
                return new TranslationRequest(open.Seq, open.Source, false);
            }
        }

        /// <summary>
        /// Stores a translation. Returns false when the sentence moved on while the request was in
        /// flight (it finished, or the session was reset) — the result is stale and must not be
        /// shown, let alone logged.
        /// </summary>
        public bool ApplyTranslation(TranslationRequest request, string notice, string translation)
        {
            lock (gate)
            {
                var segment = Find(request.Seq);
                if (segment == null || segment.IsFinal != request.IsFinal)
                    return false;

                segment.Notice = notice;
                segment.Translation = translation;
                segment.State = CaptionTranslationState.Ready;
                if (request.IsFinal)
                    segment.Logged = true;
                revision++;
                return true;
            }
        }

        /// <summary>
        /// Takes a finished sentence that has not been recorded yet, without translating it — the
        /// path used while translation is paused.
        /// </summary>
        public bool TakeUnrecordedFinal(out int seq, out string source)
        {
            lock (gate)
            {
                foreach (var segment in segments)
                {
                    if (!segment.IsFinal || segment.Logged || segment.Source.Length == 0)
                        continue;

                    segment.Logged = true;
                    segment.State = CaptionTranslationState.Ready;
                    seq = segment.Seq;
                    source = segment.Source;
                    revision++;
                    return true;
                }
            }

            seq = 0;
            source = string.Empty;
            return false;
        }

        /// <summary>The last <paramref name="maxLines"/> sentences, oldest first.</summary>
        public CaptionSnapshot BuildSnapshot(int maxLines, string liveNotice)
        {
            int count = Math.Max(1, maxLines);

            lock (gate)
            {
                var lines = new List<CaptionLine>(count);
                for (int i = segments.Count - 1; i >= 0 && lines.Count < count; i--)
                {
                    var segment = segments[i];
                    if (segment.Source.Length == 0)
                        continue;

                    lines.Add(new CaptionLine
                    {
                        Seq = segment.Seq,
                        Source = segment.Source,
                        Translation = segment.Translation,
                        Notice = segment.Notice,
                        IsFinal = segment.IsFinal,
                        IsLive = i == segments.Count - 1 && !segment.IsFinal
                    });
                }
                lines.Reverse();

                return new CaptionSnapshot
                {
                    Lines = lines,
                    LiveNotice = liveNotice,
                    Revision = ++revision
                };
            }
        }

        private CaptionSegment? Find(int seq)
        {
            foreach (var segment in segments)
            {
                if (segment.Seq == seq)
                    return segment;
            }
            return null;
        }

        private CaptionSegment GetOrCreateOpen()
        {
            if (segments.Count > 0 && !segments[^1].IsFinal)
                return segments[^1];
            return NewSegment();
        }

        private CaptionSegment NewSegment()
        {
            var segment = new CaptionSegment(nextSeq++, string.Empty);
            segments.Add(segment);
            return segment;
        }

        private static void Close(CaptionSegment segment, long now)
        {
            segment.IsFinal = true;
            // Reset, so a translation that was requested while the sentence was still open does
            // not prevent the (longer) finished text from being translated and logged.
            segment.State = CaptionTranslationState.None;
            segment.LastChangeTicks = now;
        }

        private void Trim()
        {
            while (segments.Count > MAX_SEGMENTS && segments[0].IsFinal && segments[0].Logged)
                segments.RemoveAt(0);
        }
    }

    internal sealed class CaptionSegment
    {
        public CaptionSegment(int seq, string source)
        {
            Seq = seq;
            Source = source;
        }

        public int Seq { get; }
        public string Source { get; set; }
        public string Translation { get; set; } = string.Empty;
        public string Notice { get; set; } = string.Empty;
        public bool IsFinal { get; set; }
        public bool Logged { get; set; }
        public CaptionTranslationState State { get; set; }

        public long LastChangeTicks { get; set; } = Environment.TickCount64;
        public long LastRequestTicks { get; set; }
        public string LastRequestText { get; set; } = string.Empty;
    }
}
