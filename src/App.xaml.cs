using System.Windows;

using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator
{
    public partial class App : Application
    {
        App()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Translator.Setting?.Save();

            // The sync loop blocks on UIA calls, so keep it on its own thread instead of a pool thread.
            Task.Factory.StartNew(Translator.SyncLoop, TaskCreationOptions.LongRunning);
            Task.Run(() => Translator.TranslateLoop());
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
