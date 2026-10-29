using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using Wpf.Ui.Controls;

namespace LiveCaptionsTranslator
{
    public partial class SettingPage : Page
    {
        // Fallback height, used only until the grid has been measured. The window is sized to the
        // content in `FitWindowToContent`, so this value is not what the page normally opens at.
        public const int PAGE_HEIGHT = 400;

        // Space the window frame takes above the page (the title bar row) plus a pixel of slack so
        // the bottom of the last control is never flushed against the window edge.
        private const double CHROME_HEIGHT = 27 + 2;
        private const int MIN_PAGE_HEIGHT = 190;

        private static SettingWindow? SettingWindow;

        public SettingPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();

            // Populate before selecting, and select the entries that match the saved families so the
            // boxes reflect the current configuration. Source and translation fonts are independent:
            // the audio language and the target language rarely share a script.
            var overlayState = Translator.Setting.OverlayWindow;

            SubtitleFontBox.ItemsSource = Utils.CaptionFontPresets.Translation;
            SubtitleFontBox.SelectedItem = Utils.CaptionFontPresets.Translation.FirstOrDefault(
                p => p.Family == overlayState.CaptionFontFamily) ?? Utils.CaptionFontPresets.Translation[0];

            SourceFontBox.ItemsSource = Utils.CaptionFontPresets.Source;
            SourceFontBox.SelectedItem = Utils.CaptionFontPresets.Source.FirstOrDefault(
                p => p.Family == (overlayState.CaptionSourceFontFamily ?? string.Empty))
                ?? Utils.CaptionFontPresets.Source[0];

            DataContext = Translator.Setting;

            Loaded += (s, e) =>
            {
                FitWindowToContent();
                CheckForFirstUse();
            };

            TranslateAPIBox.ItemsSource = Translator.Setting?.Configs.Keys;
            TranslateAPIBox.SelectedIndex = 0;

            LoadAPISetting();
        }

        // The window used to be pinned at a hard-coded 400 DIPs, tuned to the old layout that stacked
        // five settings in one column. With three compact rows the grid is ~187 DIPs tall, so the
        // bottom half of the window was empty. Measuring the grid instead keeps the window exactly as
        // tall as its content, and it follows the content if the layout or the text size changes.
        // Everything here is in device-independent pixels, so display scaling does not break it.
        private void FitWindowToContent()
        {
            // The page is inside a ScrollViewer: it is laid out with infinite height, so the grid
            // reports exactly what it needs rather than the space it was given.
            SettingsGrid.UpdateLayout();

            double content = SettingsGrid.ActualHeight;
            if (content <= 0)
                return;

            double pageHeight = content + SettingsGrid.Margin.Top + SettingsGrid.Margin.Bottom;
            int targetHeight = (int)Math.Ceiling(pageHeight + CHROME_HEIGHT);
            if (targetHeight < MIN_PAGE_HEIGHT)
                targetHeight = MIN_PAGE_HEIGHT;

            App.Settings?.AutoHeightAdjust(
                minHeight: targetHeight, maxHeight: targetHeight);
        }


        private void LiveCaptionsButton_click(object sender, RoutedEventArgs e)
        {
            if (Translator.Window == null)
                return;

            var button = sender as Wpf.Ui.Controls.Button;
            var text = ButtonText.Text;

            bool isHide = Translator.Window.Current.BoundingRectangle == Rect.Empty;
            if (isHide)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                ButtonText.Text = "Hide";
            }
            else
            {
                LiveCaptionsHandler.HideLiveCaptions(Translator.Window);
                ButtonText.Text = "Show";
            }
        }

        private void TranslateAPIBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadAPISetting();
        }

        private void SubtitleFontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SubtitleFontBox.SelectedItem is not Utils.CaptionFontPreset preset)
                return;

            Translator.Setting.OverlayWindow.CaptionFontFamily = preset.Family;
        }

        private void SourceFontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceFontBox.SelectedItem is not Utils.CaptionFontPreset preset)
                return;

            // Empty means "inherit the translation font", which keeps the previous behaviour.
            Translator.Setting.OverlayWindow.CaptionSourceFontFamily = preset.Family;
        }

        private void TargetLangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TargetLangBox.SelectedItem != null)
                Translator.Setting.TargetLanguage = TargetLangBox.SelectedItem.ToString();
        }

        private void TargetLangBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Translator.Setting.TargetLanguage = TargetLangBox.Text;
        }

        private void APISettingButton_click(object sender, RoutedEventArgs e)
        {
            if (SettingWindow != null && SettingWindow.IsLoaded)
                SettingWindow.Activate();
            else
            {
                SettingWindow = new SettingWindow();
                SettingWindow.Closed += (sender, args) => SettingWindow = null;
                SettingWindow.Show();
            }
        }

        private void Contexts_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (Translator.Setting.DisplaySentences > Translator.Setting.NumContexts)
                Translator.Setting.DisplaySentences = Translator.Setting.NumContexts;
        }

        private void DisplaySentences_ValueChanged(object sender, NumberBoxValueChangedEventArgs args)
        {
            if (Translator.Setting.DisplaySentences > Translator.Setting.NumContexts)
                Translator.Setting.NumContexts = Translator.Setting.DisplaySentences;

            // `DisplaySentences` lives on the root setting, not on the overlay state, so nothing
            // refreshes the overlay on its own. Re-publishing the snapshot makes the change visible
            // immediately instead of at the next caption update.
            Translator.Republish();
        }

        private void LiveCaptionsInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            LiveCaptionsInfoFlyout.Show();
        }

        private void LiveCaptionsInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            LiveCaptionsInfoFlyout.Hide();
        }

        private void FrequencyInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            FrequencyInfoFlyout.Show();
        }

        private void FrequencyInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            FrequencyInfoFlyout.Hide();
        }

        private void TranslateAPIInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            TranslateAPIInfoFlyout.Show();
        }

        private void TranslateAPIInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            TranslateAPIInfoFlyout.Hide();
        }

        private void TargetLangInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            TargetLangInfoFlyout.Show();
        }

        private void TargetLangInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            TargetLangInfoFlyout.Hide();
        }

        private void CaptionLogMaxInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            CaptionLogMaxInfoFlyout.Show();
        }

        private void CaptionLogMaxInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            CaptionLogMaxInfoFlyout.Hide();
        }

        private void ContextAwareInfo_MouseEnter(object sender, MouseEventArgs e)
        {
            ContextAwareInfoFlyout.Show();
        }

        private void ContextAwareInfo_MouseLeave(object sender, MouseEventArgs e)
        {
            ContextAwareInfoFlyout.Hide();
        }

        private void CheckForFirstUse()
        {
            if (Translator.FirstUseFlag)
                ButtonText.Text = "Hide";
        }

        public void LoadAPISetting()
        {
            var configType = Translator.Setting[Translator.Setting.ApiName].GetType();
            var languagesProp = configType.GetProperty(
                "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);

            // Traverse base classes to find `SupportedLanguages`
            while (configType != null && languagesProp == null)
            {
                configType = configType.BaseType;
                languagesProp = configType.GetProperty(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);
            }
            if (languagesProp == null)
                languagesProp = typeof(TranslateAPIConfig).GetProperty(
                    "SupportedLanguages", BindingFlags.Public | BindingFlags.Static);

            var supportedLanguages = (Dictionary<string, string>)languagesProp.GetValue(null);
            TargetLangBox.ItemsSource = supportedLanguages.Keys;

            string targetLang = Translator.Setting.TargetLanguage;
            if (!supportedLanguages.ContainsKey(targetLang))
                supportedLanguages[targetLang] = targetLang;    // add custom language to supported languages
            TargetLangBox.SelectedItem = targetLang;
        }
    }
}