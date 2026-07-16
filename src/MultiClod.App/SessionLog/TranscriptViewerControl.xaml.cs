using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MultiClod.App.SessionLog.Rendering;
using MultiClod.App.SessionLog.Tailing;

namespace MultiClod.App.SessionLog;

/// <summary>
/// Reusable transcript-viewer body: a JSONL file path in, live rendered/tailed rows out. Owned by
/// SessionLogWindow, but deliberately independent of it - a future consumer could host this
/// control anywhere a "show me this jsonl file, live" view is useful. SetSource fully tears down
/// and rebuilds the tailer/factory for an explicit source switch; a same-source live update never
/// goes through SetSource again.
/// </summary>
public partial class TranscriptViewerControl : UserControl
{
    private const int InitialLoadBatchSize = 200;
    private const double AtBottomEpsilonPixels = 4.0;
    private static readonly TimeSpan BannerFadeDuration = TimeSpan.FromMilliseconds(200);

    private readonly ObservableCollection<TranscriptRowViewModel> rows = new();
    private TranscriptRowFactory factory = new();
    private TranscriptFileTailer? tailer;
    private bool showAllEvents;
    private bool isAtBottom = true;
    private int newArrivalsSinceScroll;

    public TranscriptViewerControl()
    {
        this.InitializeComponent();

        var view = CollectionViewSource.GetDefaultView(this.rows);
        view.Filter = this.FilterRow;
        this.RowsItemsControl.ItemsSource = view;

        this.CommandBindings.Add(new CommandBinding(SessionLogCommands.CopyEntryJson, this.OnCopyEntryJsonExecuted));
    }

    public void SetSource(string? filePath)
    {
        this.tailer?.Dispose();
        this.tailer = null;
        this.rows.Clear();
        this.factory = new TranscriptRowFactory();
        this.isAtBottom = true;
        this.newArrivalsSinceScroll = 0;
        this.HideNewArrivalsBanner();
        this.AccessProblemText.Visibility = Visibility.Collapsed;

        if (filePath is null)
        {
            this.WaitingText.Visibility = Visibility.Visible;
            this.RowsScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        var newTailer = new TranscriptFileTailer(filePath);
        newTailer.StateChanged += this.OnTailerStateChanged;
        newTailer.LinesAvailable += this.OnLinesAvailable;
        newTailer.AccessProblemChanged += this.OnAccessProblemChanged;
        this.tailer = newTailer;
        this.OnTailerStateChanged(newTailer.State);
    }

    private void OnTailerStateChanged(TranscriptTailerState state)
    {
        this.Dispatcher.Invoke(() =>
        {
            var waiting = state != TranscriptTailerState.Tailing;
            this.WaitingText.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            this.RowsScrollViewer.Visibility = waiting ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void OnLinesAvailable(IReadOnlyList<string> lines)
    {
        this.Dispatcher.Invoke(() => this.AppendLinesInBatches(lines));
    }

    // Processes the first batch synchronously (so the tailer's callback returns promptly), then
    // yields back to the dispatcher between remaining batches - opening the log on a large
    // existing transcript stays responsive instead of freezing the window for one giant update.
    private void AppendLinesInBatches(IReadOnlyList<string> lines)
    {
        var index = 0;

        void ProcessNextBatch()
        {
            var end = Math.Min(index + InitialLoadBatchSize, lines.Count);
            for (; index < end; index++)
            {
                foreach (var row in this.factory.ProcessLine(lines[index]))
                {
                    this.AppendRow(row);
                }
            }

            if (index < lines.Count)
            {
                this.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ProcessNextBatch));
            }
        }

        ProcessNextBatch();
    }

    private void AppendRow(TranscriptRowViewModel row)
    {
        this.rows.Add(row);
        if (!this.isAtBottom)
        {
            this.newArrivalsSinceScroll++;
            this.ShowNewArrivalsBanner();
        }
    }

    private void OnAccessProblemChanged(bool hasProblem)
    {
        this.Dispatcher.Invoke(() => this.AccessProblemText.Visibility = hasProblem ? Visibility.Visible : Visibility.Collapsed);
    }

    private void OnRowsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var wasAtBottom = this.isAtBottom;
        this.isAtBottom = e.VerticalOffset >= this.RowsScrollViewer.ScrollableHeight - AtBottomEpsilonPixels;
        if (this.isAtBottom && !wasAtBottom)
        {
            this.newArrivalsSinceScroll = 0;
            this.HideNewArrivalsBanner();
        }
    }

    private void ShowNewArrivalsBanner()
    {
        this.NewArrivalsText.Text = this.newArrivalsSinceScroll == 1 ? "1 new message" : $"{this.newArrivalsSinceScroll} new messages";
        this.NewArrivalsBanner.Visibility = Visibility.Visible;
        this.NewArrivalsBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, BannerFadeDuration));
    }

    private void HideNewArrivalsBanner()
    {
        var animation = new DoubleAnimation(0.0, BannerFadeDuration);
        animation.Completed += (_, _) => this.NewArrivalsBanner.Visibility = Visibility.Collapsed;
        this.NewArrivalsBanner.BeginAnimation(OpacityProperty, animation);
    }

    private void OnNewArrivalsBannerClicked(object sender, MouseButtonEventArgs e)
    {
        this.RowsScrollViewer.ScrollToBottom();
    }

    private void OnShowAllEventsChanged(object sender, RoutedEventArgs e)
    {
        this.showAllEvents = this.ShowAllEventsCheckBox.IsChecked == true;
        CollectionViewSource.GetDefaultView(this.rows).Refresh();
    }

    private bool FilterRow(object item) =>
        this.showAllEvents || item is not TranscriptRowViewModel { Category: TranscriptRowCategory.SystemMeta };

    private void OnCopyEntryJsonExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is TranscriptRowViewModel row)
        {
            Clipboard.SetText(row.CopyableJson);
        }
    }
}
