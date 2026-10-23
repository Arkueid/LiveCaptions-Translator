using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace LiveCaptionsTranslator
{
    public class StrokeDecorator : Decorator
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(StrokeDecorator),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(StrokeDecorator),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        // Rendering is cached: the overlay rebuilds its inlines several times per second and both the
        // text layout and `FormattedText.BuildGeometry` (glyph outline extraction) are expensive.
        private FormattedText? cachedText;
        private Geometry? cachedGeometry;
        private ulong cachedKey;
        private bool hasCache;

        private Pen? cachedPen;
        private Brush? cachedPenBrush;
        private double cachedPenThickness;

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);

            hasCache = false;

            if (visualRemoved is TextBlock oldTextBlock)
            {
                UnregisterTextBlockListeners(oldTextBlock);
                UnregisterRunListeners(oldTextBlock);
            }

            if (visualAdded is TextBlock newTextBlock)
            {
                RegisterTextBlockListeners(newTextBlock);
                RegisterRunListeners(newTextBlock);
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Child is not TextBlock textBlock)
            {
                base.OnRender(drawingContext);
                return;
            }

            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            ulong key = ComputeCacheKey(textBlock, pixelsPerDip);
            if (!hasCache || key != cachedKey)
            {
                cachedKey = key;
                hasCache = true;
                cachedText = BuildFormattedText(textBlock, pixelsPerDip);
                cachedGeometry = null;
            }

            if (cachedText == null)
                return;

            if (StrokeThickness > 0)
            {
                // Built lazily on purpose: the default configuration has no stroke at all, so most
                // caption updates never pay for a geometry build.
                cachedGeometry ??= cachedText.BuildGeometry(new Point(0, 0));
                drawingContext.DrawGeometry(null, GetStrokePen(), cachedGeometry);
            }

            drawingContext.DrawText(cachedText, new Point(0, 0));
        }

        private static FormattedText BuildFormattedText(TextBlock textBlock, double pixelsPerDip)
        {
            var typeface = new Typeface(
                textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch);

            FormattedText formattedText;

            if (textBlock.Inlines != null && textBlock.Inlines.Count > 0)
            {
                // Inlines, process each Run. LineBreak inlines become "\n" so that
                // multi-line captions keep their per-line formatting.
                var builder = new StringBuilder();
                var runs = new List<(Run Run, int Start, int Length)>();
                int position = 0;
                foreach (var inline in textBlock.Inlines)
                {
                    switch (inline)
                    {
                        case Run run:
                            string runText = run.Text ?? string.Empty;
                            if (runText.Length == 0)
                                break;
                            runs.Add((run, position, runText.Length));
                            builder.Append(runText);
                            position += runText.Length;
                            break;
                        case LineBreak:
                            builder.Append('\n');
                            position += 1;
                            break;
                    }
                }

                formattedText = new FormattedText(
                    builder.ToString(),
                    CultureInfo.CurrentUICulture,
                    textBlock.FlowDirection,
                    typeface,
                    textBlock.FontSize,
                    textBlock.Foreground,
                    pixelsPerDip);

                ApplyLayoutSettings(formattedText, textBlock);

                foreach (var (run, start, length) in runs)
                {
                    formattedText.SetFontFamily(run.FontFamily ?? textBlock.FontFamily, start, length);
                    formattedText.SetFontSize(GetValue(run, Run.FontSizeProperty, textBlock.FontSize), start, length);
                    formattedText.SetFontWeight(GetValue(run, Run.FontWeightProperty, textBlock.FontWeight), start,
                        length);
                    formattedText.SetFontStyle(GetValue(run, Run.FontStyleProperty, textBlock.FontStyle), start, length);
                    formattedText.SetFontStretch(GetValue(run, Run.FontStretchProperty, textBlock.FontStretch), start,
                        length);
                    formattedText.SetForegroundBrush(run.Foreground ?? textBlock.Foreground, start, length);
                }
            }
            else
            {
                // No Inlines, process the TextBlock
                formattedText = new FormattedText(
                    textBlock.Text ?? string.Empty,
                    CultureInfo.CurrentUICulture,
                    textBlock.FlowDirection,
                    typeface,
                    textBlock.FontSize,
                    textBlock.Foreground,
                    pixelsPerDip);

                ApplyLayoutSettings(formattedText, textBlock);

                if (textBlock.TextWrapping == TextWrapping.NoWrap)
                    formattedText.MaxLineCount = 1;
            }

            return formattedText;
        }

        private static void ApplyLayoutSettings(FormattedText formattedText, TextBlock textBlock)
        {
            formattedText.TextAlignment = textBlock.TextAlignment;
            formattedText.Trimming = textBlock.TextTrimming;
            formattedText.MaxTextWidth = textBlock.ActualWidth > 0 ? textBlock.ActualWidth : double.MaxValue;

            // `MaxTextHeight` is left unset on purpose. `FormattedText` silently drops the last line
            // when the laid-out height exceeds this limit — even by a rounding step — and the overlay
            // always sets `LineHeight`, which makes the laid-out height land exactly on the measured
            // height. The result was that the final caption line (the translation) was never painted:
            // with `DisplaySentences = 1` the overlay showed the source line only.
            // This decorator only paints what its child already measured, so no height clamp is needed.

            // Keep the drawn text on the same line grid the TextBlock was measured with.
            if (!double.IsNaN(textBlock.LineHeight) && textBlock.LineHeight > 0)
                formattedText.LineHeight = textBlock.LineHeight;
        }

        private Pen GetStrokePen()
        {
            Brush brush = Stroke ?? Brushes.Black;
            double thickness = StrokeThickness;

            if (cachedPen == null ||
                !ReferenceEquals(cachedPenBrush, brush) ||
                cachedPenThickness != thickness)
            {
                cachedPen = new Pen(brush, thickness)
                {
                    DashCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                    StartLineCap = PenLineCap.Round
                };
                cachedPenBrush = brush;
                cachedPenThickness = thickness;
            }

            return cachedPen;
        }

        // Everything that changes the rendered output has to be part of the key, including the
        // measured size, because it decides where lines wrap.
        private static ulong ComputeCacheKey(TextBlock textBlock, double pixelsPerDip)
        {
            ulong hash = 14695981039346656037UL;

            void Mix(ulong value) => hash = (hash ^ value) * 1099511628211UL;
            void MixDouble(double value) => Mix((ulong)BitConverter.DoubleToInt64Bits(value));

            MixDouble(pixelsPerDip);
            MixDouble(textBlock.ActualWidth);
            MixDouble(textBlock.ActualHeight);
            MixDouble(textBlock.FontSize);
            MixDouble(double.IsNaN(textBlock.LineHeight) ? 0 : textBlock.LineHeight);
            Mix((ulong)(int)textBlock.TextAlignment);
            Mix((ulong)(int)textBlock.TextTrimming);
            Mix((ulong)(int)textBlock.TextWrapping);
            Mix((ulong)(int)textBlock.FlowDirection);
            Mix((ulong)(int)textBlock.LineStackingStrategy);
            Mix((ulong)(textBlock.FontFamily?.Source?.GetHashCode() ?? 0));
            Mix((ulong)textBlock.FontWeight.ToOpenTypeWeight());
            Mix((ulong)textBlock.FontStyle.GetHashCode());
            Mix((ulong)textBlock.FontStretch.GetHashCode());
            Mix((ulong)(textBlock.Foreground?.GetHashCode() ?? 0));

            if (textBlock.Inlines != null && textBlock.Inlines.Count > 0)
            {
                foreach (var inline in textBlock.Inlines)
                {
                    switch (inline)
                    {
                        case Run run:
                            Mix((ulong)'R');
                            Mix((ulong)((run.FontFamily ?? textBlock.FontFamily)?.Source?.GetHashCode() ?? 0));
                            MixDouble(GetValue(run, Run.FontSizeProperty, textBlock.FontSize));
                            Mix((ulong)GetValue(run, Run.FontWeightProperty, textBlock.FontWeight).ToOpenTypeWeight());
                            Mix((uint)GetValue(run, Run.FontStyleProperty, textBlock.FontStyle).GetHashCode());
                            Mix((uint)GetValue(run, Run.FontStretchProperty, textBlock.FontStretch).GetHashCode());
                            Mix((ulong)((run.Foreground ?? textBlock.Foreground)?.GetHashCode() ?? 0));
                            foreach (char c in run.Text ?? string.Empty)
                                Mix(c);
                            break;
                        case LineBreak:
                            Mix((ulong)'\n');
                            break;
                    }
                }
            }
            else
            {
                foreach (char c in textBlock.Text ?? string.Empty)
                    Mix(c);
            }

            return hash;
        }

        protected override Size MeasureOverride(Size constraint)
        {
            if (Child != null)
            {
                Child.Measure(constraint);
                return Child.DesiredSize;
            }

            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            if (Child != null)
            {
                Child.Opacity = 0;
                Child.Arrange(new Rect(arrangeSize));
            }

            return arrangeSize;
        }

        private void RegisterTextBlockListeners(TextBlock textBlock)
        {
            RegisterPropertyListener(textBlock, TextBlock.TextProperty);
            RegisterPropertyListener(textBlock, TextBlock.FontSizeProperty);
            RegisterPropertyListener(textBlock, TextBlock.FontWeightProperty);
            RegisterPropertyListener(textBlock, TextBlock.FontStyleProperty);
            RegisterPropertyListener(textBlock, TextBlock.FontFamilyProperty);
            RegisterPropertyListener(textBlock, TextBlock.FontStretchProperty);
            RegisterPropertyListener(textBlock, TextBlock.ForegroundProperty);
            RegisterPropertyListener(textBlock, TextBlock.TextAlignmentProperty);
            RegisterPropertyListener(textBlock, TextBlock.TextTrimmingProperty);
            RegisterPropertyListener(textBlock, TextBlock.TextWrappingProperty);
            RegisterPropertyListener(textBlock, TextBlock.LineHeightProperty);
        }

        private void UnregisterTextBlockListeners(TextBlock textBlock)
        {
            UnregisterPropertyListener(textBlock, TextBlock.TextProperty);
            UnregisterPropertyListener(textBlock, TextBlock.FontSizeProperty);
            UnregisterPropertyListener(textBlock, TextBlock.FontWeightProperty);
            UnregisterPropertyListener(textBlock, TextBlock.FontStyleProperty);
            UnregisterPropertyListener(textBlock, TextBlock.FontFamilyProperty);
            UnregisterPropertyListener(textBlock, TextBlock.FontStretchProperty);
            UnregisterPropertyListener(textBlock, TextBlock.ForegroundProperty);
            UnregisterPropertyListener(textBlock, TextBlock.TextAlignmentProperty);
            UnregisterPropertyListener(textBlock, TextBlock.TextTrimmingProperty);
            UnregisterPropertyListener(textBlock, TextBlock.TextWrappingProperty);
            UnregisterPropertyListener(textBlock, TextBlock.LineHeightProperty);
        }

        private void RegisterRunListeners(TextBlock textBlock)
        {
            if (textBlock.Inlines == null)
                return;
            foreach (var run in textBlock.Inlines.OfType<Run>())
            {
                RegisterPropertyListener(run, Run.ForegroundProperty);
                RegisterPropertyListener(run, Run.FontSizeProperty);
                RegisterPropertyListener(run, Run.FontWeightProperty);
                RegisterPropertyListener(run, Run.FontStyleProperty);
                RegisterPropertyListener(run, Run.FontFamilyProperty);
                RegisterPropertyListener(run, Run.FontStretchProperty);
                RegisterPropertyListener(run, Run.TextDecorationsProperty);
            }
        }

        private void UnregisterRunListeners(TextBlock textBlock)
        {
            if (textBlock.Inlines == null)
                return;
            foreach (var run in textBlock.Inlines.OfType<Run>())
            {
                UnregisterPropertyListener(run, Run.ForegroundProperty);
                UnregisterPropertyListener(run, Run.FontSizeProperty);
                UnregisterPropertyListener(run, Run.FontWeightProperty);
                UnregisterPropertyListener(run, Run.FontStyleProperty);
                UnregisterPropertyListener(run, Run.FontFamilyProperty);
                UnregisterPropertyListener(run, Run.FontStretchProperty);
                UnregisterPropertyListener(run, Run.TextDecorationsProperty);
            }
        }

        private void RegisterPropertyListener(DependencyObject obj, DependencyProperty property)
        {
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                property, obj.GetType());
            descriptor?.AddValueChanged(obj, OnTextBlockPropertyChanged);
        }

        private void UnregisterPropertyListener(DependencyObject obj, DependencyProperty property)
        {
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                property, obj.GetType());
            descriptor?.RemoveValueChanged(obj, OnTextBlockPropertyChanged);
        }

        private void OnTextBlockPropertyChanged(object sender, EventArgs e)
        {
            InvalidateVisual();
        }

        private static T GetValue<T>(DependencyObject obj, DependencyProperty property, T fallback)
        {
            return obj.ReadLocalValue(property) != DependencyProperty.UnsetValue ?
                (T)obj.GetValue(property) : fallback;
        }
    }
}
