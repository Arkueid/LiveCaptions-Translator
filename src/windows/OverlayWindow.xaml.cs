using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.apis;
using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using LiveCaptionsTranslator.Utils;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Color = System.Windows.Media.Color;
using ColorEnum = LiveCaptionsTranslator.Utils.Color;

namespace LiveCaptionsTranslator
{
    public partial class OverlayWindow : Window
    {
        private readonly Dictionary<ColorEnum, SolidColorBrush> colorMap = new()
        {
            {ColorEnum.White, Brushes.White},
            {ColorEnum.Yellow, Brushes.Yellow},
            {ColorEnum.LimeGreen, Brushes.LimeGreen},
            {ColorEnum.Aqua, Brushes.Aqua},
            {ColorEnum.Blue, Brushes.Blue},
            {ColorEnum.DeepPink, Brushes.DeepPink},
            {ColorEnum.Red, Brushes.Red},
            {ColorEnum.Black, Brushes.Black},
        };
        // A setting.json written by an older (or hand-edited) build can hold a value outside the
        // enum; falling back keeps a bad value from crashing the window at startup.
        private SolidColorBrush BrushOf(ColorEnum color) =>
            colorMap.TryGetValue(color, out var brush) ? brush : Brushes.White;

        private CaptionVisible onlyMode = CaptionVisible.Both;

        // The translation line is drawn slightly larger than the source line.
        private const double TRANSLATION_FONT_RATIO = 1.25;

        // Window height floor for the caption box (matches the XAML). The box keeps this height even
        // when it holds nothing but a one-line notice, so the overlay does not jump size at startup.
        private const double CAPTION_MIN_HEIGHT = 135;

        // The `[Paused]` / `[ERROR]` prefix keeps a monospace face so it never looks like part of
        // the translated sentence.
        private static readonly FontFamily NoticePrefixFontFamily = new("Consolas");

        private const int WM_HOTKEY = 0x0312;
        private const int CLICK_THROUGH_HOTKEY_ID = 0x0CC7;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;
        private const uint VK_T = 0x54;

        // One refresh per dispatcher pass instead of one per changed property: the display loop
        // updates two or three caption properties in a row.
        private bool refreshQueued;

        // The dimmed source-text brush only depends on the font/background colors, so it is rebuilt
        // when those change rather than on every refresh.
        private SolidColorBrush? cachedSourceBrush;
        private Color cachedSourceFontColor;
        private Color cachedSourceBackgroundColor;

        private bool clickThroughEnabled;
        private HwndSource? hwndSource;

        // `FontFamily` is created once per configured family list instead of on every refresh.
        private readonly Dictionary<string, FontFamily> fontFamilyCache = new();

        public CaptionVisible OnlyMode
        {
            get => onlyMode;
            set
            {
                onlyMode = value;
                ResizeForOnlyMode();
            }
        }
        public CaptionLocation SwitchMode { get; set; } = CaptionLocation.SubtitleTop;

        public OverlayWindow()
        {
            InitializeComponent();

            // The window is sized to its content, so vertical resizing works on this cap. Start with
            // the whole work area (i.e. "no cap") when nothing has been saved yet.
            double workAreaHeight = SystemParameters.WorkArea.Height;
            double savedMaxHeight = Translator.Setting.OverlayWindow.MaxHeight;
            MaxHeight = Math.Min(savedMaxHeight > 0 ? savedMaxHeight : workAreaHeight, workAreaHeight);

            BorderBackground.Background = BrushOf(Translator.Setting.OverlayWindow.BackgroundColor);

            ApplyFontStroke();
            ApplyBackgroundOpacity();
            RefreshCaptionText();

            Translator.Caption.PropertyChanged += Caption_PropertyChanged;
            Translator.Setting.OverlayWindow.PropertyChanged += OverlaySetting_PropertyChanged;
            Translator.PauseChanged += SyncPauseButton;
            SyncPauseButton();
            Closed += OnOverlayClosed;

            // This window is Application.Current.MainWindow now, and WPF-UI paints its theme
            // backdrop on the main window. A backdrop turns the 5 DIP margin around the caption box
            // into a visible window frame and makes the box itself look opaque, so it is stripped
            // again after every theme application (the settings window asks for one when it opens).
            ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        }

        private void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
        {
            // Changed is raised before the backdrop is applied, so the cleanup waits for that call.
            Dispatcher.BeginInvoke(new Action(EnsureTransparentChrome), DispatcherPriority.Background);
        }

        /// <summary>
        /// Restores the transparent window background. No-op when nothing was applied; safe to call
        /// at any time.
        /// </summary>
        public void EnsureTransparentChrome()
        {
            // Not RemoveBackdrop: that one "restores" the content background by painting the
            // composition surface with SystemColors.WindowColor — solid white on a light theme —
            // which then shows through the transparent margins of this window. RemoveBackground is
            // the variant that actually clears: transparent brush on the window and Colors.Transparent
            // on its composition target, which is what a layered window needs.
            WindowBackdrop.RemoveBackground(this);
            Background = Brushes.Transparent;
        }

        private void OnOverlayClosed(object? sender, EventArgs e)
        {
            Translator.Caption.PropertyChanged -= Caption_PropertyChanged;
            Translator.Setting.OverlayWindow.PropertyChanged -= OverlaySetting_PropertyChanged;
            Translator.PauseChanged -= SyncPauseButton;
            ApplicationThemeManager.Changed -= OnApplicationThemeChanged;

            WindowsAPI.UnregisterHotKey(new WindowInteropHelper(this).Handle, CLICK_THROUGH_HOTKEY_ID);
            hwndSource?.RemoveHook(WndProc);
            hwndSource = null;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Click-through cannot be undone from inside the window (it no longer receives input),
            // so a shortcut restores it.
            if (msg == WM_HOTKEY && wParam.ToInt32() == CLICK_THROUGH_HOTKEY_ID)
            {
                SetClickThrough(false);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void Caption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Caption.Snapshot))
            {
                RequestRefresh();
            }
        }

        // Every overlay style setting is applied from here instead of from the click handlers.
        // A handler that mutates the setting and forgets to apply it is precisely how the color
        // buttons ended up looking dead: the value changed, the screen did not.
        private void OverlaySetting_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(OverlayWindowState.BackgroundColor):
                    BorderBackground.Background = BrushOf(Translator.Setting.OverlayWindow.BackgroundColor);
                    ApplyBackgroundOpacity();
                    // The dimmed source line is derived from the background color as well.
                    RequestRefresh();
                    break;

                case nameof(OverlayWindowState.Opacity):
                    ApplyBackgroundOpacity();
                    break;

                case nameof(OverlayWindowState.FontStroke):
                    ApplyFontStroke();
                    break;

                case nameof(OverlayWindowState.FontSize):
                case nameof(OverlayWindowState.FontBold):
                case nameof(OverlayWindowState.FontColor):
                case nameof(OverlayWindowState.CaptionFontFamily):
                case nameof(OverlayWindowState.CaptionSourceFontFamily):
                case nameof(OverlayWindowState.LineSpacing):
                    RequestRefresh();
                    break;
            }
        }

        private FontFamily ResolveFontFamily(string configured)
        {
            string source = string.IsNullOrWhiteSpace(configured) ? CaptionFontPresets.DefaultFamily : configured;
            if (!fontFamilyCache.TryGetValue(source, out var fontFamily))
            {
                fontFamily = new FontFamily(source);
                fontFamilyCache[source] = fontFamily;
            }
            return fontFamily;
        }

        // Coalesces the burst of property changes that make up a single caption update into one
        // rebuild. `Render` priority keeps the refreshed text in the frame that is about to be drawn.
        private void RequestRefresh()
        {
            if (refreshQueued)
                return;

            refreshQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                refreshQueued = false;
                RefreshCaptionText();
            }), DispatcherPriority.Render);
        }

        // Gap between sentence pairs, as a fraction of the source font size. A blank text line used to
        // do this job, but a line box is never shorter than `LineHeight` (33.8 px at the defaults), so
        // the pairs sat a whole line apart: measured, 50 px of blank space between groups against 13 px
        // between the two lines of a group. 0.8 puts the gap at 31 px — clearly grouped, noticeably
        // tighter, and it scales with the font size.
        private const double PAIR_GAP_RATIO = 0.8;

        // One TextBlock per sentence pair, each wrapped in its own stroke decorator and reused between
        // refreshes: the overlay rebuilds its text several times per second.
        private readonly List<(StrokeDecorator Decorator, TextBlock Block)> captionBlocks = new();

        // What each block is currently showing. A refresh normally only changes the live line, so
        // rebuilding every block would re-measure and re-arrange the whole stack for nothing.
        private readonly List<string> renderedKeys = new();

        private (StrokeDecorator Decorator, TextBlock Block) GetCaptionBlock(int index)
        {
            while (captionBlocks.Count <= index)
            {
                var block = new TextBlock { FontSize = Translator.Setting.OverlayWindow.FontSize };
                var decorator = new StrokeDecorator
                {
                    Stroke = Brushes.Black,
                    StrokeThickness = Translator.Setting.OverlayWindow.FontStroke,
                    Child = block
                };
                CaptionStack.Children.Add(decorator);
                captionBlocks.Add((decorator, block));
            }
            return captionBlocks[index];
        }

        // Paints the published snapshot: one block per sentence pair (source line + its translation),
        // in the order the timeline reports them. Nothing is derived here — the display used to pair
        // "newest source" with "newest translation" and to guess which pair was already on air, which
        // is how sentence groups ended up printed twice or skipped entirely.
        private void RefreshCaptionText()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshCaptionText), DispatcherPriority.Background);
                return;
            }

            var setting = Translator.Setting.OverlayWindow;
            var snapshot = Translator.Caption.Snapshot;
            var brush = BrushOf(setting.FontColor);

            // Source lines are dimmed toward the background so that the translation stands out, but
            // they keep enough contrast to stay readable (white on a white box would vanish).
            var sourceBrush = GetSourceBrush(
                BrushOf(setting.FontColor).Color, BrushOf(setting.BackgroundColor).Color);

            double sourceSize = setting.FontSize;
            double translationSize = sourceSize * TRANSLATION_FONT_RATIO;
            var sourceWeight = setting.FontBold is Utils.FontBold.Both or Utils.FontBold.SubtitleOnly
                ? FontWeights.Bold : FontWeights.Regular;
            var translationWeight = setting.FontBold is Utils.FontBold.Both or Utils.FontBold.TranslationOnly
                ? FontWeights.Bold : FontWeights.Regular;

            // Font family and line height are applied to every pair block; each run inherits them
            // unless it overrides (the notice prefix keeps its monospace face, and the source line
            // uses its own family when one is configured, e.g. a Japanese font for Japanese audio).
            var fontFamily = ResolveFontFamily(setting.CaptionFontFamily);
            FontFamily? sourceFontFamily = string.IsNullOrWhiteSpace(setting.CaptionSourceFontFamily)
                ? null
                : ResolveFontFamily(setting.CaptionSourceFontFamily);
            // LineHeight must stay above the natural height of the largest line (the translation
            // line), otherwise the glyphs would be clipped instead of spaced.
            double lineHeight = Math.Round(translationSize * setting.LineSpacing, 1);
            double pairGap = Math.Round(sourceSize * PAIR_GAP_RATIO, 1);

            bool showSource = onlyMode != CaptionVisible.TranslationOnly;
            bool showTranslation = onlyMode != CaptionVisible.SubtitleOnly;
            bool sourceFirst = SwitchMode == CaptionLocation.SubtitleTop;

            // Everything that changes how an already built block looks, folded into the cache key.
            // The colors belong here: a color change leaves the text untouched, so without them the
            // cached key still matched and the new color only showed up with the next caption
            // update — the color buttons appeared to do nothing.
            string style = $"{sourceSize}|{translationSize}|{lineHeight}|{pairGap}|{onlyMode}|" +
                           $"{sourceFirst}|{fontFamily.Source}|{sourceFontFamily?.Source}|" +
                           $"{sourceWeight}|{translationWeight}|{(int)setting.FontColor}|" +
                           $"{(int)setting.BackgroundColor}";

            int pairIndex = 0;

            void AppendRun(TextBlock block, string? text, double fontSize, FontWeight weight,
                Brush foreground, FontFamily? family = null)
            {
                if (string.IsNullOrEmpty(text))
                    return;
                var run = new Run(text)
                {
                    FontSize = fontSize,
                    FontWeight = weight,
                    Foreground = foreground
                };
                if (family != null)
                    run.FontFamily = family;
                block.Inlines.Add(run);
            }

            // Draws one pair. `notice` is printed in front of the translation (latency tag, `[ERROR]`,
            // `[Paused]`) and keeps a monospace face so it never looks like part of the sentence.
            void AddPair(string source, string translation, string notice)
            {
                bool hasSource = showSource && !string.IsNullOrWhiteSpace(source);
                bool hasTranslation = showTranslation &&
                    (!string.IsNullOrWhiteSpace(translation) || !string.IsNullOrEmpty(notice));
                if (!hasSource && !hasTranslation)
                    return;

                var (decorator, block) = GetCaptionBlock(pairIndex);
                if (renderedKeys.Count <= pairIndex)
                    renderedKeys.Add(string.Empty);

                string key = style + '\u001f' + source + '\u001f' + translation + '\u001f' + notice;
                if (renderedKeys[pairIndex] != key)
                {
                    renderedKeys[pairIndex] = key;
                    block.Inlines.Clear();
                    block.FontSize = sourceSize;
                    block.FontFamily = fontFamily;
                    block.Foreground = brush;
                    block.TextWrapping = TextWrapping.Wrap;
                    block.LineHeight = lineHeight;
                    block.LineStackingStrategy = LineStackingStrategy.MaxHeight;

                    void AddTranslationLine()
                    {
                        if (!hasTranslation)
                            return;
                        if (block.Inlines.Count > 0)
                            block.Inlines.Add(new LineBreak());
                        if (!string.IsNullOrEmpty(notice))
                            AppendRun(block, notice + " ", translationSize, translationWeight,
                                brush, NoticePrefixFontFamily);
                        AppendRun(block, translation, translationSize, translationWeight, brush);
                    }

                    void AddSourceLine()
                    {
                        if (!hasSource)
                            return;
                        if (block.Inlines.Count > 0)
                            block.Inlines.Add(new LineBreak());
                        AppendRun(block, source, sourceSize, sourceWeight, sourceBrush, sourceFontFamily);
                    }

                    if (sourceFirst)
                    {
                        AddSourceLine();
                        AddTranslationLine();
                    }
                    else
                    {
                        AddTranslationLine();
                        AddSourceLine();
                    }

                    // Inlines are swapped wholesale, which raises no property-changed notification
                    // the decorator listens to, so the repaint has to be requested explicitly.
                    decorator.InvalidateVisual();
                }

                decorator.StrokeThickness = setting.FontStroke;
                // The gap between groups lives here, not in a blank text line — and it has to be set
                // on the decorator: a Decorator arranges its child into its own bounds and ignores
                // the child's margin, so a margin on the block would add height but move nothing.
                decorator.Margin = new Thickness(0, pairIndex == 0 ? 0 : pairGap, 0, 0);
                decorator.Visibility = Visibility.Visible;
                pairIndex++;
            }

            var lines = snapshot.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                bool newest = i == lines.Count - 1;
                string notice = newest && !string.IsNullOrEmpty(snapshot.LiveNotice)
                    ? snapshot.LiveNotice
                    : line.Notice;
                AddPair(line.Source, line.Translation, notice);
            }

            if (lines.Count == 0 && !string.IsNullOrEmpty(snapshot.LiveNotice))
                AddPair(string.Empty, string.Empty, snapshot.LiveNotice);

            // Blocks left over from a previous refresh (fewer sentences on screen now) must not keep
            // taking space.
            for (int i = pairIndex; i < captionBlocks.Count; i++)
                captionBlocks[i].Decorator.Visibility = Visibility.Collapsed;

            // The height floor is fixed: an empty or notice-only box stays the same size as a
            // caption-filled one. It used to shrink to a single line in that state, which made the
            // overlay start out as a narrow strip and grow on the first caption.
            MinHeight = CAPTION_MIN_HEIGHT;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        // The window height follows its content (`SizeToContent="Height"`), so setting `Height` directly
        // would be overwritten on the next layout pass. The vertical handles therefore move the cap.
        private void TopThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newMaxHeight = this.MaxHeight - e.VerticalChange;

            if (newMaxHeight < this.MinHeight || newMaxHeight > MaxHeightLimit)
                return;

            this.MaxHeight = newMaxHeight;
            this.Top += e.VerticalChange;
            SaveMaxHeight();
        }

        private void BottomThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newMaxHeight = this.MaxHeight + e.VerticalChange;

            if (newMaxHeight < this.MinHeight || newMaxHeight > MaxHeightLimit)
                return;

            this.MaxHeight = newMaxHeight;
            SaveMaxHeight();
        }

        private static double MaxHeightLimit => SystemParameters.WorkArea.Height;

        private void SaveMaxHeight()
        {
            Translator.Setting.OverlayWindow.MaxHeight =
                double.IsInfinity(this.MaxHeight) ? 0 : Math.Round(this.MaxHeight, 1);
        }

        private void LeftThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width - e.HorizontalChange;

            if (newWidth >= this.MinWidth)
            {
                this.Left += e.HorizontalChange;
                this.Width = newWidth;
            }
        }

        private void RightThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;

            if (newWidth >= this.MinWidth)
            {
                this.Width = newWidth;
            }
        }

        private void TopLeftThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            TopThumb_OnDragDelta(sender, e);
            LeftThumb_OnDragDelta(sender, e);
        }

        private void TopRightThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            TopThumb_OnDragDelta(sender, e);
            RightThumb_OnDragDelta(sender, e);
        }

        private void BottomLeftThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            BottomThumb_OnDragDelta(sender, e);
            LeftThumb_OnDragDelta(sender, e);
        }

        private void BottomRightThumb_OnDragDelta(object sender, DragDeltaEventArgs e)
        {
            BottomThumb_OnDragDelta(sender, e);
            RightThumb_OnDragDelta(sender, e);
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            FadeControlPanel(show: true);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            FadeControlPanel(show: false);
        }

        // The tools fade in while the pointer is over the overlay and fade out when it leaves. They
        // start invisible and not hit-testable, so they never cover the captions unless they are
        // actually wanted — and an invisible bar can never swallow a click aimed at a caption.
        private void FadeControlPanel(bool show)
        {
            if (clickThroughEnabled)
                return;

            ControlPanel.IsHitTestVisible = show;
            var fade = new DoubleAnimation(show ? 1d : 0d, TimeSpan.FromMilliseconds(140));
            ControlPanel.BeginAnimation(OpacityProperty, fade);
        }

        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            Translator.TogglePause();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            App.OpenSettings();
        }

        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            App.Quit();
        }

        // The pause state can also be changed from the main window, so the shape is re-derived
        // rather than remembered: bars while translation runs, a triangle while it is paused.
        private void SyncPauseButton()
        {
            PauseIcon.Symbol = Translator.LogOnlyFlag ? SymbolRegular.Play20 : SymbolRegular.Pause20;
            PauseButton.ToolTip = Translator.LogOnlyFlag ? "Resume Translation" : "Pause Translation";
        }

        private void FontIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.FontSize + StyleConsts.DELTA_FONT_SIZE < StyleConsts.MAX_FONT_SIZE)
                Translator.Setting.OverlayWindow.FontSize += StyleConsts.DELTA_FONT_SIZE;
        }

        private void FontDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.FontSize - StyleConsts.DELTA_FONT_SIZE > StyleConsts.MIN_FONT_SIZE)
                Translator.Setting.OverlayWindow.FontSize -= StyleConsts.DELTA_FONT_SIZE;
        }

        private void FontBold_Click(object sender, RoutedEventArgs e)
        {
            var bold = Translator.Setting.OverlayWindow.FontBold;
            Translator.Setting.OverlayWindow.FontBold =
                bold == Utils.FontBold.Both ? Utils.FontBold.None : bold + 1;
        }

        private void FontStrokeIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.FontStroke + StyleConsts.DELTA_STROKE > StyleConsts.MAX_STROKE)
                return;
            Translator.Setting.OverlayWindow.FontStroke += StyleConsts.DELTA_STROKE;
        }

        private void FontStrokeDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.FontStroke - StyleConsts.DELTA_STROKE < StyleConsts.MIN_STROKE)
                return;
            Translator.Setting.OverlayWindow.FontStroke -= StyleConsts.DELTA_STROKE;
        }

        private void FontColorCycle_Click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.OverlayWindow.FontColor = NextColor(Translator.Setting.OverlayWindow.FontColor);
        }

        private void BackgroundOpacityIncrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.Opacity + StyleConsts.DELTA_OPACITY < StyleConsts.MAX_OPACITY)
                Translator.Setting.OverlayWindow.Opacity += StyleConsts.DELTA_OPACITY;
            else
                Translator.Setting.OverlayWindow.Opacity = StyleConsts.MAX_OPACITY;
        }

        private void BackgroundOpacityDecrease_Click(object sender, RoutedEventArgs e)
        {
            if (Translator.Setting.OverlayWindow.Opacity - StyleConsts.DELTA_OPACITY > StyleConsts.MIN_OPACITY)
                Translator.Setting.OverlayWindow.Opacity -= StyleConsts.DELTA_OPACITY;
            else
                Translator.Setting.OverlayWindow.Opacity = StyleConsts.MIN_OPACITY;
        }

        private void BackgroundColorCycle_Click(object sender, RoutedEventArgs e)
        {
            Translator.Setting.OverlayWindow.BackgroundColor =
                NextColor(Translator.Setting.OverlayWindow.BackgroundColor);
        }

        // The next value is computed before it is stored. `Color++` writes the out-of-range value
        // first and only then wraps it back, and the property change it raises in between reaches
        // this window: a `colorMap[9]` lookup thrown from a click handler is what that looked like.
        private static ColorEnum NextColor(ColorEnum current)
        {
            var next = (ColorEnum)((int)current + 1);
            return next > ColorEnum.Black ? ColorEnum.White : next;
        }

        private void OnlyModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (onlyMode == CaptionVisible.SubtitleOnly)
            {
                // (0) Subtitle + Translation
                OnlyModeIcon.Symbol = SymbolRegular.PanelBottom20;
                OnlyMode = CaptionVisible.Both;
            }
            else if (onlyMode == CaptionVisible.Both)
            {
                // (1) Translation Only
                OnlyModeIcon.Symbol = SymbolRegular.PanelTopExpand20;
                OnlyMode = CaptionVisible.TranslationOnly;
            }
            else
            {
                // (2) Subtitle Only
                OnlyModeIcon.Symbol = SymbolRegular.PanelTopContract20;
                OnlyMode = CaptionVisible.SubtitleOnly;
            }
        }

        private void SwitchModeButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchMode = SwitchMode == CaptionLocation.TranslationTop
                ? CaptionLocation.SubtitleTop
                : CaptionLocation.TranslationTop;
            RefreshCaptionText();
        }

        private void ClickThrough_Click(object sender, RoutedEventArgs e)
        {
            SetClickThrough(!clickThroughEnabled);
        }

        private void SetClickThrough(bool enabled)
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = WindowsAPI.GetWindowLong(hwnd, WindowsAPI.GWL_EXSTYLE);

            // The shortcut is the only way back out of click-through (the window stops receiving
            // input), so if it cannot be registered the mode is refused instead of being entered
            // with no exit. The overlay used to be re-creatable from the main window; now it is the
            // shell, so there is nothing left to rebuild it from.
            if (enabled && !WindowsAPI.RegisterHotKey(hwnd, CLICK_THROUGH_HOTKEY_ID,
                    MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_T))
            {
                Debug.WriteLine("[OverlayWindow] Ctrl+Alt+T could not be registered; " +
                                "click-through stays off.");
                return;
            }

            WindowsAPI.SetWindowLong(hwnd, WindowsAPI.GWL_EXSTYLE,
                enabled
                    ? extendedStyle | WindowsAPI.WS_EX_TRANSPARENT
                    : extendedStyle & ~WindowsAPI.WS_EX_TRANSPARENT);

            clickThroughEnabled = enabled;
            // Click-through means the pointer never reaches this window, so the tools could never
            // be faded back in; hide them outright until the shortcut turns it off.
            ControlPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
            if (!enabled)
            {
                WindowsAPI.UnregisterHotKey(hwnd, CLICK_THROUGH_HOTKEY_ID);
                FadeControlPanel(show: false);
            }

            ClickThroughIcon.Symbol = enabled ? SymbolRegular.LockClosed16 : SymbolRegular.LockOpen16;
        }

        public void ResizeForOnlyMode()
        {
            RefreshCaptionText();
        }

        public void ApplyFontStroke()
        {
            // Applied to the existing pair blocks and to the ones created by the next refresh.
            double thickness = Translator.Setting.OverlayWindow.FontStroke;
            foreach (var (decorator, _) in captionBlocks)
                decorator.StrokeThickness = thickness;
        }

        public void ApplyBackgroundOpacity()
        {
            Color color = ((SolidColorBrush)BorderBackground.Background).Color;
            BorderBackground.Background = new SolidColorBrush(Color.FromArgb(
                (byte)Translator.Setting.OverlayWindow.Opacity, color.R, color.G, color.B));
        }

        // Dimmed source-line brush, blended toward the background so the translation stands out.
        // A minimum contrast is enforced because the old pure blend made the source line disappear
        // whenever the font and background colors were close (white text on a white box).
        private SolidColorBrush GetSourceBrush(Color fontColor, Color backgroundColor)
        {
            if (cachedSourceBrush != null &&
                cachedSourceFontColor == fontColor &&
                cachedSourceBackgroundColor == backgroundColor)
                return cachedSourceBrush;

            Color dimmed = Blend(fontColor, backgroundColor, 0.35);
            if (ContrastRatio(dimmed, backgroundColor) < 1.8)
            {
                dimmed = Luminance(backgroundColor) > 0.5
                    ? Color.FromRgb(0x2B, 0x2B, 0x2B)
                    : Color.FromRgb(0xD8, 0xD8, 0xD8);
            }

            cachedSourceBrush = new SolidColorBrush(dimmed);
            cachedSourceBrush.Freeze();
            cachedSourceFontColor = fontColor;
            cachedSourceBackgroundColor = backgroundColor;
            return cachedSourceBrush;
        }

        private static Color Blend(Color foreground, Color background, double amount) =>
            Color.FromRgb(
                (byte)Math.Clamp(foreground.R + (background.R - foreground.R) * amount, 0, 255),
                (byte)Math.Clamp(foreground.G + (background.G - foreground.G) * amount, 0, 255),
                (byte)Math.Clamp(foreground.B + (background.B - foreground.B) * amount, 0, 255));

        private static double Luminance(Color color) =>
            0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);

        private static double ToLinear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static double ContrastRatio(Color a, Color b)
        {
            double l1 = Luminance(a);
            double l2 = Luminance(b);
            if (l1 < l2)
                (l1, l2) = (l2, l1);
            return (l1 + 0.05) / (l2 + 0.05);
        }
    }
}