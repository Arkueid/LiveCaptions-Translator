using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public static class Translator
    {
        // Caption updates are pushed by UI Automation property-changed events. The loop below only
        // wakes up on a real change, on a slow reconciliation poll (providers may drop events), or to
        // run the timed triggers — no cross-process UIA read happens while the text is static.
        private const int WAIT_SLICE_MS = 200;
        // Probe often until events are proven to work, then reconcile rarely.
        private const int RECONCILE_PROBE_MS = 400;
        private const int RECONCILE_POLL_MS = 2500;
        private const int SUBSCRIBE_RETRY_MS = 10000;
        private const int RECONNECT_DELAY_MS = 2000;
        // Fallback cadence: the previous polling loop, kept for providers without usable events.
        private const int FALLBACK_ACTIVE_POLL_MS = 50;
        private const int FALLBACK_IDLE_POLL_MS = 150;
        private const int MAX_MISSED_EVENTS = 3;

        private static AutomationElement? window = null;
        private static Caption? caption = null;
        private static Setting? setting = null;

        // The display pipeline, in order:
        //   LiveCaptions buffer -> TranscriptStabilizer -> CaptionTimeline -> CaptionSnapshot
        // The stabilizer decides which text may no longer change, the timeline gives every finished
        // sentence an identity and the snapshot is the single object the overlay paints.
        private static readonly TranscriptStabilizer stabilizer = new();
        private static readonly CaptionTimeline timeline = new();

        // Only one request is ever in flight: local models degrade badly when requests pile up, and
        // a live caption only cares about the freshest result. Because the loop is sequential, the
        // order in which results are applied (and written to the history) is the sentence order.
        private static CancellationTokenSource? inFlight;

        // A partial request is speculation: it describes a sentence that is still growing and may
        // be replaced a moment later. When a finished sentence appears, that speculation is
        // cancelled so the GPU starts on the text a reader will actually keep. Only the in-flight
        // flag is read from the caption thread, which is why it is separate from `inFlight`.
        private static volatile bool inFlightIsPartial;

        // Which row the history ends with, so a partial translation of the *same* sentence replaces
        // its own row instead of piling up.
        private static int lastLoggedSeq;
        private static bool lastLoggedWasPartial;

        // Signalled by the UI Automation event handler whenever LiveCaptions updates its caption text.
        private static readonly AutoResetEvent captionChangedSignal = new(false);
        private static long nextSubscribeAttemptTicks = 0;

        public static AutomationElement? Window
        {
            get => window;
            set
            {
                window = value;
                // A new window means the old automation element (and its event handler) is gone.
                if (value == null)
                {
                    LiveCaptionsHandler.ResetCaptionSubscription();
                    // The next captions belong to a new LiveCaptions session; the previous
                    // transcript must not be continued as if it were the same conversation.
                    if (caption != null)
                        ClearContexts();
                }
            }
        }
        public static Caption? Caption => caption;
        public static Setting? Setting => setting;

        // Paused by default so that launching the app never fires up an LLM on its own.
        private static bool logOnlyFlag = true;

        public static bool LogOnlyFlag
        {
            get => logOnlyFlag;
            set
            {
                if (logOnlyFlag == value)
                    return;
                logOnlyFlag = value;
                PauseChanged?.Invoke();
            }
        }

        /// <summary>Raised whenever <see cref="LogOnlyFlag"/> flips; the overlay mirrors the shape.</summary>
        public static event Action? PauseChanged;

        /// <summary>
        /// The single place a pause toggle happens from (the overlay's toolbar drives it), so a
        /// pause always means the same thing: stop calling the API, and drop what is on screen.
        /// </summary>
        public static void TogglePause()
        {
            LogOnlyFlag = !LogOnlyFlag;
            ResetDisplay();
            ClearContexts();
        }
        public static bool FirstUseFlag { get; set; } = false;

        // A partial sentence is translated only after it has stopped growing for this long, so a
        // sentence that arrives in ten pieces is translated once instead of ten times.
        private static int PartialDebounceMs => Math.Max(0, Setting?.PartialDebounceMs ?? 300);
        // ...but never leave the translation silent longer than this while speech continues.
        private static int ForceRefreshMs => Math.Max(0, Setting?.ForceRefreshMs ?? 1500);
        private static int IdleTriggerMs => (Setting?.MaxIdleInterval ?? 50) * FALLBACK_IDLE_POLL_MS;

        public static event Action? TranslationLogged;

        static Translator()
        {
            window = LiveCaptionsHandler.LaunchLiveCaptions();
            LiveCaptionsHandler.FixLiveCaptions(Window);
            LiveCaptionsHandler.HideLiveCaptions(Window);

            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), models.Setting.FILENAME)))
                FirstUseFlag = true;

            caption = Caption.GetInstance();
            setting = Setting.Load();
            LogOnlyFlag = setting.StartPaused;
        }

        public static void SyncLoop()
        {
            string lastFullText = string.Empty;
            long lastPollTicks = 0;
            bool eventDriven = false;
            bool eventSourceDisabled = false;
            int missedEvents = 0;
            int eventsSeen = 0;
            int quietReconciles = 0;

            while (true)
            {
                if (Window == null)
                {
                    Thread.Sleep(RECONNECT_DELAY_MS);
                    continue;
                }

                if (!eventSourceDisabled)
                {
                    if (!eventDriven)
                    {
                        // Retry occasionally: the caption element may not exist yet right after launch.
                        if (Environment.TickCount64 >= nextSubscribeAttemptTicks)
                        {
                            if (LiveCaptionsHandler.TrySubscribeCaptionChanges(Window, OnCaptionChanged))
                            {
                                eventDriven = true;
                                lastPollTicks = Environment.TickCount64;
                                Debug.WriteLine("[SyncLoop] Caption events subscribed.");
                            }
                            else
                                nextSubscribeAttemptTicks = Environment.TickCount64 + SUBSCRIBE_RETRY_MS;
                        }
                    }
                    else if (!LiveCaptionsHandler.IsCaptionChangeSubscribed &&
                             !LiveCaptionsHandler.TrySubscribeCaptionChanges(Window, OnCaptionChanged))
                    {
                        // The element was re-created (LiveCaptions restarted) and is not ready yet.
                        eventDriven = false;
                        nextSubscribeAttemptTicks = Environment.TickCount64 + SUBSCRIBE_RETRY_MS;
                    }
                }

                bool signaled = false;
                if (eventDriven)
                {
                    signaled = captionChangedSignal.WaitOne(WAIT_SLICE_MS);

                    // Until events have been proven to work (or while nothing is being said) poll
                    // more often, so a silent provider is detected quickly instead of quietly
                    // falling behind the captions.
                    int reconcileMs = eventsSeen >= 2 || quietReconciles >= 3
                        ? RECONCILE_POLL_MS
                        : RECONCILE_PROBE_MS;

                    if (!signaled && Environment.TickCount64 - lastPollTicks < reconcileMs)
                    {
                        // Nothing to read from LiveCaptions; only the timed triggers can fire here.
                        CheckTimedTriggers();
                        continue;
                    }

                    // An event may have arrived between the wait and the read below.
                    signaled |= captionChangedSignal.WaitOne(0);
                }

                long now = Environment.TickCount64;
                bool textChanged = false;
                try
                {
                    // Get the text recognized by LiveCaptions
                    string fullText = LiveCaptionsHandler.GetCaptions(Window);
                    lastPollTicks = now;

                    if (!string.IsNullOrEmpty(fullText) &&
                        string.CompareOrdinal(fullText, lastFullText) != 0)
                    {
                        // A change seen without a corresponding event means the provider dropped it.
                        if (eventDriven && !signaled)
                        {
                            if (++missedEvents >= MAX_MISSED_EVENTS)
                            {
                                // Events are not delivered reliably; do not try them again this session.
                                Debug.WriteLine("[SyncLoop] Caption events look unreliable, falling back to polling.");
                                eventDriven = false;
                                eventSourceDisabled = true;
                                missedEvents = 0;
                                LiveCaptionsHandler.UnsubscribeCaptionChanges();
                            }
                        }
                        else if (signaled)
                        {
                            missedEvents = 0;
                            eventsSeen++;
                        }

                        quietReconciles = 0;
                        lastFullText = fullText;
                        UpdateCaption(fullText);
                        textChanged = true;
                    }
                    else if (!signaled)
                        quietReconciles++;
                }
                catch (ElementNotAvailableException)
                {
                    Window = null;
                    continue;
                }
                catch (Exception ex)
                {
                    // Preprocessing must never kill this loop silently.
                    Debug.WriteLine($"[SyncLoop] {ex}");
                }

                CheckTimedTriggers();

                // Provider has no usable events: keep the original poll cadence.
                if (!eventDriven)
                    Thread.Sleep(textChanged ? FALLBACK_ACTIVE_POLL_MS : FALLBACK_IDLE_POLL_MS);
            }
        }

        private static void OnCaptionChanged()
        {
            // Raised on a UI Automation worker thread; only signal, never touch UI state here.
            try
            {
                captionChangedSignal.Set();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// Feeds one caption buffer into the pipeline. The only thing that reaches the screen is
        /// text the stabilizer has declared stable; see <see cref="TranscriptStabilizer"/>.
        /// </summary>
        private static void UpdateCaption(string fullText)
        {
            string normalized = NormalizeCaption(fullText);
            if (normalized.Length == 0)
                return;

            long now = Environment.TickCount64;
            string committed = stabilizer.Update(normalized, now);
            if (committed.Length == 0)
                return;

            timeline.Append(committed, now);
            PreemptPartialForFinal();
            PublishSnapshot();
        }

        /// <summary>
        /// Cleans up what LiveCaptions emits. Term normalization happens here rather than on each
        /// finished segment, because the stabilizer compares the raw hypotheses with each other: a
        /// word that was rewritten on one side would otherwise break the alignment and re-commit
        /// text that is already on screen.
        /// </summary>
        private static string NormalizeCaption(string fullText)
        {
            fullText = RegexPatterns.Acronym().Replace(fullText, "$1$2");
            fullText = RegexPatterns.AcronymWithWords().Replace(fullText, "$1 $2");
            fullText = RegexPatterns.PunctuationSpace().Replace(fullText, "$1 ");
            fullText = RegexPatterns.CJPunctuationSpace().Replace(fullText, "$1");
            // LiveCaptions wraps its captions wherever the box ends and puts a `\n` there. Those
            // breaks are display wraps, not sentence ends, so they are joined back into continuous
            // text: sentence ends come from real punctuation only.
            fullText = TextUtil.JoinCaptionLines(fullText);
            return TextUtil.NormalizeSpokenTerms(fullText).Trim();
        }

        private static void CheckTimedTriggers()
        {
            long now = Environment.TickCount64;

            // Nothing else can prove the tail stable once the updates stop: commit it.
            string flushed = stabilizer.FlushQuiet(now);
            if (flushed.Length > 0)
            {
                timeline.Append(flushed, now);
                PreemptPartialForFinal();
                PublishSnapshot();
            }

            // Long silence: the speaker finished the sentence without any punctuation.
            if (timeline.OpenIdleMs(now) >= IdleTriggerMs && timeline.FinalizeOpen(now))
            {
                PreemptPartialForFinal();
                PublishSnapshot();
            }
        }

        /// <summary>
        /// Drops a speculative (still-growing sentence) request as soon as a finished sentence is
        /// waiting. Without this the loop would finish translating a snapshot that is about to be
        /// thrown away — with one local GPU that is pure delay for the line the reader is on.
        /// </summary>
        private static void PreemptPartialForFinal()
        {
            if (!inFlightIsPartial || !timeline.HasPendingFinal())
                return;

            try
            {
                inFlight?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The loop already finished with it; nothing to interrupt.
            }
        }

        public static async Task TranslateLoop()
        {
            while (true)
            {
                // Check LiveCaptions.exe still alive
                if (Window == null)
                {
                    Window = LiveCaptionsHandler.LaunchLiveCaptions();
                }

                if (LogOnlyFlag)
                {
                    // Paused: still record the finished sentences, but never call the API.
                    if (timeline.TakeUnrecordedFinal(out _, out string pausedText))
                    {
                        await LogOnly(pausedText);
                        PublishSnapshot();
                    }
                    await Task.Delay(20);
                    continue;
                }

                var request = NextRequest();
                if (request == null)
                {
                    await Task.Delay(20);
                    continue;
                }

                string translated;
                using (var cts = new CancellationTokenSource())
                {
                    inFlight = cts;
                    inFlightIsPartial = !request.IsFinal;
                    try
                    {
                        (translated, _) = await Translate(request.Text, request.IsFinal, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TranslateLoop] {ex}");
                        continue;
                    }
                    finally
                    {
                        if (ReferenceEquals(inFlight, cts))
                        {
                            inFlight = null;
                            inFlightIsPartial = false;
                        }
                    }
                }

                var (notice, body) = SplitNotice(translated);

                // The sentence may have finished (or the session been reset) while the request was
                // in flight; then this result belongs to a line that no longer exists.
                if (!timeline.ApplyTranslation(request, notice, body))
                    continue;

                if (request.IsFinal)
                {
                    await Log(request.Text, translated);
                    RememberContext(request.Text, body);
                    lastLoggedSeq = request.Seq;
                    lastLoggedWasPartial = false;
                }
                else if (Setting?.LogPartialTranslations == true)
                {
                    // A mid-sentence translation only replaces the row this same sentence already
                    // wrote — never a row belonging to an earlier sentence.
                    bool overwrite = lastLoggedWasPartial && lastLoggedSeq == request.Seq;
                    await Log(request.Text, translated, overwrite);
                    lastLoggedSeq = request.Seq;
                    lastLoggedWasPartial = true;
                }

                PublishSnapshot();
            }
        }

        private static TranslationRequest? NextRequest()
        {
            // Finished sentences first: they are what the reader actually reads.
            return timeline.NextFinalRequest() ??
                   timeline.NextLiveRequest(PartialDebounceMs, ForceRefreshMs, Environment.TickCount64);
        }

        /// <summary>
        /// Splits the model's `[notice] text` shape once, so the overlay only has to place two
        /// strings. An error keeps its whole message on the translation line.
        /// </summary>
        private static (string Notice, string Body) SplitNotice(string translated)
        {
            string text = (translated ?? string.Empty).Trim();
            if (text.Length == 0)
                return (string.Empty, string.Empty);

            var match = RegexPatterns.NoticePrefixAndTranslation().Match(text);
            if (match.Success)
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());

            return (string.Empty, text);
        }

        /// <summary>Rebuilds and publishes the snapshot the overlay paints.</summary>
        public static void PublishSnapshot()
        {
            var current = Caption;
            if (current == null)
                return;

            int count = Math.Max(1, Setting?.DisplaySentences ?? 1);
            current.Publish(timeline.BuildSnapshot(count, LogOnlyFlag ? "[Paused]" : string.Empty));
        }

        /// <summary>Re-publishes the current snapshot, e.g. after `DisplaySentences` changed.</summary>
        public static void Republish() => PublishSnapshot();

        /// <summary>
        /// Remembers a finished pair for the context window. It is taken from the timeline instead of
        /// being read back from the database: the row we would have read is the one we just wrote,
        /// and reading it cost two extra queries per sentence.
        /// </summary>
        private static void RememberContext(string source, string translation)
        {
            var current = Caption;
            if (current == null)
                return;

            long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var entry = new TranslationHistoryEntry
            {
                Timestamp = seconds.ToString(CultureInfo.InvariantCulture),
                TimestampFull = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                SourceText = source,
                TranslatedText = translation,
                TargetLanguage = Setting?.TargetLanguage ?? "N/A",
                ApiUsed = Setting?.ApiName ?? "N/A"
            };

            if (current.Contexts.Count >= Caption.MAX_CONTEXTS)
                current.Contexts.Dequeue();
            current.Contexts.Enqueue(entry);
        }

        public static async Task<(string, bool)> Translate(string text, bool isFinal, CancellationToken token = default)
        {
            string translatedText;
            // `isChoke` now means "this segment is finished" rather than "its last character is a
            // full stop": a segment cut at a clause break is just as finished as one ending in 「。」.
            bool isChoke = isFinal;

            try
            {
                TranslateAPI.ResetMetrics();
                var sw = Setting.MainWindow.LatencyShow ? Stopwatch.StartNew() : null;

                if (Setting.ContextAware && !TranslateAPI.IsLLMBased)
                {
                    translatedText = await TranslateAPI.TranslateFunction($"{Caption.AwareContextsCaption} 🔤 {text} 🔤", token);
                    translatedText = RegexPatterns.TargetSentence().Match(translatedText).Groups[1].Value;
                }
                else
                {
                    translatedText = await TranslateAPI.TranslateFunction(text, token);
                    translatedText = translatedText.Replace("🔤", "");
                }

                if (sw != null)
                {
                    sw.Stop();
                    translatedText = $"[{FormatLatency(sw.ElapsedMilliseconds)}] " + translatedText;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ($"[ERROR] Translation Failed: {ex.Message}", isChoke);
            }

            return (translatedText, isChoke);
        }

        /// <summary>
        /// The `[  812 ms (62+750)]` tag. When the provider reports prompt and generation time
        /// separately, both are printed: `62+750` means 62 ms of prompt processing and 750 ms of
        /// token generation. That split is the only way to tell whether prompt caching can help
        /// (a big first number) or whether the model itself is simply slow (a big second one).
        /// </summary>
        private static string FormatLatency(long totalMs)
        {
            long prefill = TranslateAPI.LastPromptMs;
            long decode = TranslateAPI.LastEvalMs;
            string split = prefill >= 0 && decode >= 0 ? $" ({prefill}+{decode})" : string.Empty;
            return $"{totalMs,4} ms{split}";
        }

        public static async Task Log(string originalText, string translatedText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            string targetLanguage, apiName;
            if (Setting != null)
            {
                targetLanguage = Setting.TargetLanguage;
                apiName = Setting.ApiName;
            }
            else
            {
                targetLanguage = "N/A";
                apiName = "N/A";
            }

            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, translatedText, targetLanguage, apiName);
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] Logging history failed.", ex.Message, SnackbarType.Error,
                    timeout: 2, closeButton: true);
            }
        }

        public static async Task LogOnly(string originalText,
            bool isOverwrite = false, CancellationToken token = default)
        {
            try
            {
                if (isOverwrite)
                    await SQLiteHistoryLogger.DeleteLastTranslation(token);
                await SQLiteHistoryLogger.LogTranslation(originalText, "N/A", "N/A", "LogOnly");
                TranslationLogged?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SnackbarHost.Show("[ERROR] Logging history failed.", ex.Message, SnackbarType.Error,
                                   timeout: 2, closeButton: true);
            }
        }

        public static void ClearContexts()
        {
            Caption?.Contexts.Clear();
            stabilizer.Reset();
            timeline.Reset();
            inFlight?.Cancel();
            PublishSnapshot();
        }

        // Called when translation is paused or resumed: whatever is in flight belongs to the state
        // we just left, so it must not pop up afterwards.
        public static void ResetDisplay()
        {
            inFlight?.Cancel();
            PublishSnapshot();
        }
    }
}
