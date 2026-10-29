using System.Diagnostics;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Navigation;
using Wpf.Ui.Appearance;

namespace LiveCaptionsTranslator
{
    public partial class InfoPage : Page
    {
        // Rows: an intro that wraps to a few lines, a separator, then one line per link.
        public const int MIN_HEIGHT = 270;

        public InfoPage()
        {
            InitializeComponent();
            ApplicationThemeManager.ApplySystemTheme();
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            Version.Text = version;

            Loaded += (s, e) =>
            {
                App.Settings?.AutoHeightAdjust(minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);
            };
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
