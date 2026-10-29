using Wpf.Ui.Controls;

namespace LiveCaptionsTranslator
{
    class SnackbarHost
    {
        public static Snackbar? mainSnackbar;
        public static MainWindow? mainWindow => App.Settings;

        public static void Show(string title = "", string message = "", SnackbarType type = SnackbarType.Info,
                                int width = 500, int timeout = 1, bool closeButton = false)
        {
            // The snackbar lives on the settings window, which no longer exists at startup. Reports
            // that happen before it is first opened (a failed history write, for instance) are only
            // logged, since there is nothing on screen to attach them to.
            var window = mainWindow;
            if (window == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SnackbarHost] {title} {message}");
                return;
            }

            ControlAppearance appearance;
            SymbolIcon icon;
            Snackbar? snackbar;

            switch (type)
            {
                case SnackbarType.Warning:
                    appearance = ControlAppearance.Caution;
                    icon = new SymbolIcon(SymbolRegular.Alert24);
                    break;
                case SnackbarType.Error:
                    appearance = ControlAppearance.Danger;
                    icon = new SymbolIcon(SymbolRegular.DismissCircle24);
                    break;
                case SnackbarType.Success:
                    appearance = ControlAppearance.Success;
                    icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24);
                    break;
                default:
                    appearance = ControlAppearance.Secondary;
                    icon = new SymbolIcon(SymbolRegular.Info24);
                    break;
            }

            mainSnackbar ??= new Snackbar(window.snackbarHost);
            snackbar = mainSnackbar;

            snackbar.SetCurrentValue(Snackbar.TitleProperty, title);
            snackbar.SetCurrentValue(System.Windows.Controls.ContentControl.ContentProperty, message);
            snackbar.SetCurrentValue(Snackbar.AppearanceProperty, appearance);
            snackbar.SetCurrentValue(Snackbar.IconProperty, icon);
            snackbar.SetCurrentValue(Snackbar.TimeoutProperty, TimeSpan.FromSeconds(timeout));

            snackbar.MinWidth = width;
            snackbar.IsCloseButtonEnabled = closeButton;

            snackbar.Show(true);
        }
    }

    public enum SnackbarType
    {
        Warning,
        Error,
        Success,
        Info
    }
}
