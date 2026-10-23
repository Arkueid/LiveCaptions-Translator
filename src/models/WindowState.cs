using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveCaptionsTranslator.Utils;

namespace LiveCaptionsTranslator.models
{
    public class MainWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool topmost = true;
        private bool captionLogEnabled = false;
        private bool latencyShow = false;
        private int originalFontSize = 15;
        private int translatedFontSize = 18;

        public bool Topmost
        {
            get => topmost;
            set
            {
                topmost = value;
                OnPropertyChanged("Topmost");
            }
        }
        public bool CaptionLogEnabled
        {
            get => captionLogEnabled;
            set
            {
                captionLogEnabled = value;
                OnPropertyChanged("CaptionLogEnabled");
            }
        }
        public bool LatencyShow
        {
            get => latencyShow;
            set
            {
                latencyShow = value;
                OnPropertyChanged("LatencyShow");
            }
        }
        public int OriginalFontSize
        {
            get => originalFontSize;
            set
            {
                originalFontSize = value;
                OnPropertyChanged("OriginalFontSize");
            }
        }
        public int TranslatedFontSize
        {
            get => translatedFontSize;
            set
            {
                translatedFontSize = value;
                OnPropertyChanged("TranslatedFontSize");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.SaveDebounced();
        }
    }

    public class OverlayWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int fontSize = 15;
        private Color fontColor = Color.White;
        private FontBold fontBold = FontBold.None;
        private double fontStroke = 0.0;
        private string captionFontFamily = CaptionFontPresets.DefaultFamily;
        private string captionSourceFontFamily = string.Empty;
        private double lineSpacing = 1.5;

        private Color backgroundColor = Color.Black;
        private int opacity = 150;
        private double maxHeight = 0;

        public int FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                OnPropertyChanged("FontSize");
            }
        }
        public Color FontColor
        {
            get => fontColor;
            set
            {
                fontColor = value;
                OnPropertyChanged("FontColor");
            }
        }
        public FontBold FontBold
        {
            get => fontBold;
            set
            {
                fontBold = value;
                OnPropertyChanged("FontBold");
            }
        }
        public double FontStroke
        {
            get => fontStroke;
            set
            {
                fontStroke = value;
                OnPropertyChanged("FontStroke");
            }
        }

        // Font family (fallback list) of both the subtitle and the translation lines.
        public string CaptionFontFamily
        {
            get => captionFontFamily;
            set
            {
                captionFontFamily = string.IsNullOrWhiteSpace(value) ? CaptionFontPresets.DefaultFamily : value;
                OnPropertyChanged("CaptionFontFamily");
            }
        }

        // Font family of the recognised line only. Empty means "follow CaptionFontFamily".
        // Kept separate so a Japanese source can be drawn with a Japanese font while the Chinese
        // translation keeps Chinese glyph forms.
        public string CaptionSourceFontFamily
        {
            get => captionSourceFontFamily;
            set
            {
                captionSourceFontFamily = value?.Trim() ?? string.Empty;
                OnPropertyChanged("CaptionSourceFontFamily");
            }
        }

        // Line height as a multiple of the translation font size. 1.2 is tight, 1.45 is airy.
        public double LineSpacing
        {
            get => lineSpacing;
            set
            {
                lineSpacing = Math.Clamp(value, 1.25, 2.2);
                OnPropertyChanged("LineSpacing");
            }
        }
        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                OnPropertyChanged("BackgroundColor");
            }
        }
        public int Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                OnPropertyChanged("Opacity");
            }
        }

        // Height cap of the overlay window, which sizes itself to its content. 0 means "not set yet".
        public double MaxHeight
        {
            get => maxHeight;
            set
            {
                maxHeight = value;
                OnPropertyChanged("MaxHeight");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.SaveDebounced();
        }
    }
}