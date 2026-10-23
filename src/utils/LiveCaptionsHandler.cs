using System.Diagnostics;
using System.Windows.Automation;

using LiveCaptionsTranslator.apis;

namespace LiveCaptionsTranslator.utils
{
    public static class LiveCaptionsHandler
    {
        public static readonly string PROCESS_NAME = "LiveCaptions";

        private static AutomationElement? captionsTextBlock = null;

        // UI Automation event subscription. LiveCaptions raises a Name property-changed event
        // whenever the caption text updates, which lets callers react to a change instead of
        // polling the automation tree every 50 ms.
        private static AutomationElement? subscribedTextBlock = null;
        private static AutomationPropertyChangedEventHandler? nameChangedHandler = null;

        public static bool IsCaptionChangeSubscribed =>
            subscribedTextBlock != null &&
            captionsTextBlock != null &&
            ReferenceEquals(subscribedTextBlock, captionsTextBlock);

        public static AutomationElement LaunchLiveCaptions()
        {
            // Init
            KillAllProcessesByPName(PROCESS_NAME);
            var process = Process.Start(PROCESS_NAME);

            // Search for window
            AutomationElement? window = null;
            for (int attemptCount = 0;
                 window == null || window.Current.ClassName.CompareTo("LiveCaptionsDesktopWindow") != 0;
                 attemptCount++)
            {
                window = FindWindowByPId(process.Id);
                if (attemptCount > 200)
                    throw new Exception("Failed to launch LiveCaptions!");
                // Avoid spinning the CPU while the window is being created.
                Thread.Sleep(50);
            }

            return window;
        }

        public static void KillLiveCaptions(AutomationElement window)
        {
            // Search for process
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            WindowsAPI.GetWindowThreadProcessId(hWnd, out int processId);
            var process = Process.GetProcessById(processId);

            // Kill process
            process.Kill();
            process.WaitForExit();
        }

        public static void HideLiveCaptions(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            int exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE);

            WindowsAPI.ShowWindow(hWnd, WindowsAPI.SW_MINIMIZE);
            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE, exStyle | WindowsAPI.WS_EX_TOOLWINDOW);
        }

        public static void RestoreLiveCaptions(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);
            int exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE);

            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE, exStyle & ~WindowsAPI.WS_EX_TOOLWINDOW);
            WindowsAPI.ShowWindow(hWnd, WindowsAPI.SW_RESTORE);
            WindowsAPI.SetForegroundWindow(hWnd);
        }

        public static void FixLiveCaptions(AutomationElement window)
        {
            nint hWnd = new nint((long)window.Current.NativeWindowHandle);

            RECT rect;
            if (!WindowsAPI.GetWindowRect(hWnd, out rect))
                throw new Exception("Unable to get the window rectangle of LiveCaptions!");
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            int x = rect.Left;
            int y = rect.Top;

            bool isSuccess = true;
            if (x < 0 || y < 0 || width < 100 || height < 100)
                isSuccess = WindowsAPI.MoveWindow(hWnd, 800, 600, 600, 200, true);
            if (!isSuccess)
                throw new Exception("Failed to fix LiveCaptions!");
        }

        public static string GetCaptions(AutomationElement window)
        {
            if (captionsTextBlock == null)
                captionsTextBlock = FindElementByAId(window, "CaptionsTextBlock");
            try
            {
                return captionsTextBlock?.Current.Name ?? string.Empty;
            }
            catch (ElementNotAvailableException)
            {
                ResetCaptionSubscription();
                throw;
            }
        }

        /// <summary>
        /// Subscribes to caption text changes through UI Automation events.
        /// Returns false when the caption element is not available yet or the provider
        /// does not support property-changed events; the caller should then keep polling.
        /// </summary>
        public static bool TrySubscribeCaptionChanges(AutomationElement? window, Action onCaptionChanged)
        {
            if (window == null || onCaptionChanged == null)
                return false;

            if (captionsTextBlock == null)
            {
                try
                {
                    captionsTextBlock = FindElementByAId(window, "CaptionsTextBlock");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LiveCaptionsHandler] Caption element lookup failed: {ex.Message}");
                    captionsTextBlock = null;
                }
            }

            if (captionsTextBlock == null)
                return false;

            // Already listening on the element this method resolved — nothing to do.
            if (IsCaptionChangeSubscribed)
                return true;

            UnsubscribeCaptionChanges();

            try
            {
                var handler = new AutomationPropertyChangedEventHandler((sender, e) =>
                {
                    if (e.Property == AutomationElement.NameProperty)
                        onCaptionChanged();
                });

                Automation.AddAutomationPropertyChangedEventHandler(
                    captionsTextBlock, TreeScope.Element, handler, AutomationElement.NameProperty);

                nameChangedHandler = handler;
                subscribedTextBlock = captionsTextBlock;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveCaptionsHandler] Caption event subscribe failed: {ex.Message}");
                nameChangedHandler = null;
                subscribedTextBlock = null;
                return false;
            }
        }

        public static void UnsubscribeCaptionChanges()
        {
            var element = subscribedTextBlock;
            var handler = nameChangedHandler;
            nameChangedHandler = null;
            subscribedTextBlock = null;

            if (element == null || handler == null)
                return;

            try
            {
                Automation.RemoveAutomationPropertyChangedEventHandler(element, handler);
            }
            catch (Exception ex)
            {
                // The process may be gone already; there is nothing left to detach from.
                Debug.WriteLine($"[LiveCaptionsHandler] Caption event unsubscribe failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Drops the cached element and the subscription. Call after LiveCaptions restarts.
        /// </summary>
        public static void ResetCaptionSubscription()
        {
            UnsubscribeCaptionChanges();
            captionsTextBlock = null;
        }

        private static AutomationElement FindWindowByPId(int processId)
        {
            var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
            return AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
        }

        public static AutomationElement? FindElementByAId(
            AutomationElement window, string automationId, CancellationToken token = default)
        {
            try
            {
                PropertyCondition condition = new PropertyCondition(
                    AutomationElement.AutomationIdProperty, automationId);
                return window.FindFirst(TreeScope.Descendants, condition);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (NullReferenceException)
            {
                return null;
            }
        }

        public static void PrintAllElementsAId(AutomationElement window)
        {
            var treeWalker = TreeWalker.RawViewWalker;
            var stack = new Stack<AutomationElement>();
            stack.Push(window);

            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (!string.IsNullOrEmpty(element.Current.AutomationId))
                    Console.WriteLine(element.Current.AutomationId);

                var child = treeWalker.GetFirstChild(element);
                while (child != null)
                {
                    stack.Push(child);
                    child = treeWalker.GetNextSibling(child);
                }
            }
        }

        public static bool ClickSettingsButton(AutomationElement window)
        {
            var settingsButton = FindElementByAId(window, "SettingsButton");
            if (settingsButton != null)
            {
                var invokePattern = settingsButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
                if (invokePattern != null)
                {
                    invokePattern.Invoke();
                    return true;
                }
            }
            return false;
        }

        private static void KillAllProcessesByPName(string processName)
        {
            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0)
                return;
            foreach (Process process in processes)
            {
                process.Kill();
                process.WaitForExit();
            }
        }
    }
}
