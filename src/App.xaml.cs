using System.Windows;
using Wpf.Ui.Appearance;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        /// <summary>
        /// The overlay is the shell: it opens with the app, is the main window (so closing it quits)
        /// and owns the only captions. The settings window is created on demand from its toolbar.
        /// </summary>
        public static OverlayWindow? Overlay { get; private set; }

        /// <summary>The settings window, i.e. what used to be the main window. Hidden, not closed.</summary>
        public static MainWindow? Settings { get; private set; }

        /// <summary>True once the process is going away, so windows stop cancelling their close.</summary>
        public static bool IsQuitting { get; private set; }

        App()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Translator.Setting?.Save();

            // The sync loop blocks on UIA calls, so keep it on its own thread instead of a pool thread.
            Task.Factory.StartNew(Translator.SyncLoop, TaskCreationOptions.LongRunning);
            Task.Run(() => Translator.TranslateLoop());
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // The settings window is created (hidden) before the overlay on purpose: it becomes
            // Application.Current.MainWindow, and WPF-UI paints its theme background on the main
            // window. That window wants a Mica backdrop; keeping the transparent overlay out of that
            // path is what keeps it transparent.
            ApplicationThemeManager.ApplySystemTheme();
            EnsureSettingsWindow();
            ShowOverlay();

            // The app starts paused (no API call until the play button is pressed). Publishing the
            // empty snapshot here makes that state visible from the first frame instead of only when
            // something else happens to republish.
            Translator.PublishSnapshot();

            // A fresh install has no API configured yet, so captions would stay empty; the settings
            // window is the only way to fix that, so it opens itself the first time.
            if (Translator.FirstUseFlag)
                OpenSettings();
        }

        public static void ShowOverlay()
        {
            if (Overlay != null)
            {
                Overlay.Show();
                Overlay.Activate();
                return;
            }

            var overlay = new OverlayWindow();
            overlay.SizeChanged += (s, e) => WindowHandler.SaveState(overlay, Translator.Setting);
            overlay.LocationChanged += (s, e) => WindowHandler.SaveState(overlay, Translator.Setting);
            overlay.Closed += (s, e) =>
            {
                if (ReferenceEquals(Overlay, overlay))
                    Overlay = null;

                // Shutdown mode is explicit, so closing the overlay has to end the process by hand:
                // without it the app would keep running with no window and no way back.
                if (!IsQuitting)
                    Quit();
            };

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            var state = WindowHandler.LoadState(overlay, Translator.Setting);
            if (state.Left <= 0 || state.Left >= screenWidth ||
                state.Top <= 0 || state.Top >= screenHeight)
            {
                WindowHandler.RestoreState(overlay, new Rect(
                    (screenWidth - 650) / 2, screenHeight * 5 / 6 - 135, 650, 135));
            }
            else
                WindowHandler.RestoreState(overlay, state);

            Overlay = overlay;
            overlay.Show();

            // When the overlay is created while the settings window is already up, it steps aside so
            // it cannot cover it; closing the settings window gives the top spot back.
            if (Settings != null && Settings.IsVisible)
                overlay.Topmost = false;
        }

        /// <summary>
        /// Creates the settings window if it does not exist yet, without showing it. Creating it up
        /// front also makes it the application's main window, which is what keeps the theme's
        /// background off the transparent overlay.
        /// </summary>
        private static MainWindow EnsureSettingsWindow()
        {
            if (Settings != null)
                return Settings;

            var settings = new MainWindow();
            settings.Closed += (s, e) =>
            {
                if (ReferenceEquals(Settings, settings))
                    Settings = null;
            };

            Settings = settings;
            return settings;
        }

        /// <summary>Shows the settings window, creating it the first time; also brings it forward.</summary>
        public static void OpenSettings()
        {
            // The overlay is topmost by design, which would leave the settings window buried under
            // it; it steps aside while the settings are open and takes the top spot back on close.
            if (Overlay != null)
                Overlay.Topmost = false;

            var settings = EnsureSettingsWindow();

            if (!settings.IsVisible)
                settings.Show();
            if (settings.WindowState == WindowState.Minimized)
                settings.WindowState = WindowState.Normal;

            settings.Activate();

            // A theme application can repaint the overlay's composition surface, so the transparent
            // chrome is re-asserted whenever the settings window comes up.
            Overlay?.EnsureTransparentChrome();
        }

        public static void RestoreOverlayTopmost()
        {
            if (Overlay != null)
                Overlay.Topmost = true;
        }

        public static void Quit()
        {
            IsQuitting = true;
            Current.Shutdown();
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            // Flush any debounced settings before the process goes away.
            try
            {
                Translator.Setting?.Save();
            }
            catch
            {
            }

            if (Translator.Window != null)
            {
                LiveCaptionsHandler.RestoreLiveCaptions(Translator.Window);
                LiveCaptionsHandler.KillLiveCaptions(Translator.Window);
            }
        }
    }
}
