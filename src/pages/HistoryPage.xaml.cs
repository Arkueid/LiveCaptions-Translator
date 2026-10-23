using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using LiveCaptionsTranslator.models;
using LiveCaptionsTranslator.utils;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LiveCaptionsTranslator
{
    public partial class HistoryPage : Page
    {
        public const int MIN_HEIGHT = 300;

        private int currentPage = 1;
        private int searchPage = 1;
        private int maxPage = 1;
        private int maxRowPerPage = 10;

        // The grid is bound once to this collection: a full page load replaces its contents, and a
        // newly logged sentence is prepended without touching the rows already on screen.
        private readonly ObservableCollection<TranslationHistoryEntry> historyItems = new();

        // Guards against an older page load overwriting a newer one (clicking Next twice quickly).
        private int loadGeneration;

        // Coalesces the burst of log notifications that arrives while a sentence is being translated.
        private long lastLiveRefreshTicks;

        // Never null: the search handler assigns whatever the box reports, and the live-refresh path
        // below reads Length on a background thread.
        private string searchText = string.Empty;
        public string SearchText
        {
            get => searchText;
            set => searchText = value ?? string.Empty;
        }

        public HistoryPage()
        {
            InitializeComponent();

            // The application theme is already applied once at startup (MainWindow), so re-applying it
            // per navigation only cost time: ~12 ms on the first visit for no visible change.
            HistoryDataGrid.ItemsSource = historyItems;

            Loaded += async (s, e) =>
            {
                // Resize before the data arrives. The page used to load first and grow the window
                // afterwards, which read as "it is slow" even though the query itself is ~2 ms.
                (App.Current.MainWindow as MainWindow)?.AutoHeightAdjust(
                    minHeight: MIN_HEIGHT, maxHeight: MIN_HEIGHT);

                await LoadHistory();

                // Loaded can fire again when navigating back to a cached page.
                Translator.TranslationLogged -= OnTranslationLogged;
                Translator.TranslationLogged += OnTranslationLogged;
            };
            Unloaded += (s, e) => Translator.TranslationLogged -= OnTranslationLogged;

            HistoryMaxRow.SelectionChanged += maxRow_SelectionChanged;
        }

        private async void OnTranslationLogged()
        {
            // One notification per logged sentence. Rebuilding the page for each of them re-bound every
            // row and jumped the scroll position back to the top, which is what made the page feel slow
            // while a translation was running. Only the first row of page 1 can change, so prepend it.
            if (searchText.Length != 0 || currentPage != 1)
                return;

            long now = Environment.TickCount64;
            if (now - lastLiveRefreshTicks < 500)
                return;
            lastLiveRefreshTicks = now;

            if (historyItems.Count == 0)
            {
                await LoadHistory();
                return;
            }

            var last = await Task.Run(() => SQLiteHistoryLogger.LoadLastTranslation());
            if (last == null)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (searchText.Length != 0 || currentPage != 1)
                    return;

                if (historyItems.Count > 0 &&
                    historyItems[0].TimestampFull == last.TimestampFull &&
                    historyItems[0].SourceText == last.SourceText)
                    return;

                historyItems.Insert(0, last);
                while (historyItems.Count > maxRowPerPage)
                    historyItems.RemoveAt(historyItems.Count - 1);
            });
        }

        private async void PageDown_click(object sender, RoutedEventArgs e)
        {
            if (currentPage - 1 >= 1)
                currentPage--;
            await LoadHistory();
        }

        private async void PageUp_click(object sender, RoutedEventArgs e)
        {
            if (currentPage < maxPage)
                currentPage++;
            await LoadHistory();
        }

        private async void Delete_click(object sender, RoutedEventArgs e)
        {
            var dialogHostContainer = (Application.Current.MainWindow as MainWindow)?.DialogHostContainer;

            var dialog = new ContentDialog
            {
                Title = new TextBlock
                {
                    Text = "Do you want to delete all history?",
                    FontSize = 18,
                    FontWeight = FontWeights.Regular
                },
                Content = "This operation cannot be undone!",
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                DefaultButton = ContentDialogButton.Close,
                DialogHost = dialogHostContainer,
                Padding = new Thickness(8, 4, 8, 8),
            };

            dialogHostContainer.Visibility = Visibility.Visible;
            var result = await dialog.ShowAsync();
            dialogHostContainer.Visibility = Visibility.Collapsed;

            if (result == ContentDialogResult.Primary)
            {
                currentPage = 1;
                await SQLiteHistoryLogger.ClearHistory();
                await LoadHistory();
            }
        }

        private async void maxRow_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string tag = (e.AddedItems[0] as ComboBoxItem).Tag as string;
            maxRowPerPage = Convert.ToInt32(tag);

            await LoadHistory();

            if (currentPage > maxPage)
            {
                currentPage = maxPage;
                await LoadHistory();
            }
        }

        private async void Refresh_click(object sender, RoutedEventArgs e)
        {
            await LoadHistory();
        }

        private async void Export_click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|All file (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"exported_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    // The whole table is read for an export, so keep it off the dispatcher as well.
                    string path = saveFileDialog.FileName;
                    await Task.Run(() => SQLiteHistoryLogger.ExportToCSV(path));
                    SnackbarHost.Show("Saved Success.", $"File saved to: {saveFileDialog.FileName}", SnackbarType.Success);
                }
                catch (Exception ex)
                {
                    SnackbarHost.Show("Save Failed.", $"File saved faild:{ex.Message}", SnackbarType.Error);
                }
            }
        }

        private async void HistorySearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string searchText = (sender as AutoSuggestBox)?.Text ?? "";

            // Clear search by Ctrl+A and Delete and Enter
            if (string.IsNullOrEmpty(searchText))
            {
                SearchText = string.Empty;
                currentPage = searchPage;
            }
            else // Submit search
            {
                if (string.IsNullOrEmpty(SearchText))
                {
                    searchPage = currentPage;
                }
                SearchText = (sender as AutoSuggestBox)?.Text;
                currentPage = 1;
            }
            await LoadHistory();
        }

        private async void HistorySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            // Press X to clear search box
            if (args.Reason == AutoSuggestionBoxTextChangeReason.ProgrammaticChange)
            {
                if (!string.IsNullOrEmpty(SearchText))
                {
                    SearchText = string.Empty;
                    currentPage = searchPage;
                    await LoadHistory();
                }
            }
        }

        public async Task LoadHistory()
        {
            // Microsoft.Data.Sqlite's async methods run synchronously underneath, so awaiting the query
            // directly ran the scan and the sort on the UI thread. Hand it to the thread pool instead;
            // the connection is per-operation now, so this is safe.
            int generation = ++loadGeneration;
            int page = currentPage;
            int rowPerPage = maxRowPerPage;
            string searchText = SearchText;

            var data = await Task.Run(() =>
                SQLiteHistoryLogger.LoadHistoryAsync(page, rowPerPage, searchText));

            maxPage = (data.Item2 > 0) ? data.Item2 : 1;

            await Dispatcher.InvokeAsync(() =>
            {
                if (generation != loadGeneration)
                    return;

                // Update the bound collection in place instead of Clear()+Add(). Clearing raises a Reset,
                // which makes the DataGrid drop every row container and generate new ones; assigning by
                // index raises a Replace, so each row keeps its DataGridRow, its cells and their
                // TextBlocks and only its DataContext changes. The UI is reused, only the data is new.
                var items = data.Item1;
                int shared = Math.Min(items.Count, historyItems.Count);
                for (int i = 0; i < shared; i++)
                {
                    if (!ReferenceEquals(historyItems[i], items[i]))
                        historyItems[i] = items[i];
                }

                for (int i = shared; i < items.Count; i++)
                    historyItems.Add(items[i]);

                // A short last page (or a search with fewer hits) leaves stale rows at the tail.
                while (historyItems.Count > items.Count)
                    historyItems.RemoveAt(historyItems.Count - 1);

                PageNumber.Text = currentPage.ToString() + "/" + maxPage.ToString();
            });
        }
    }
}