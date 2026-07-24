using System.Windows;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Modeless progress/error window shown while a deeplink import fetches/extracts - see
/// MainWindow.HandleDeeplinkRequest. One window with its visual state toggled in place
/// (indeterminate -> determinate, then either closed on success or turned into an error message),
/// rather than separate windows per state.
/// </summary>
public partial class DeeplinkProgressWindow : Window, IProgress<DeeplinkFetchProgress>
{
    public DeeplinkProgressWindow(string source)
    {
        this.InitializeComponent();
        this.StatusText.Text = $"Downloading session from {source}...";
    }

    public void Report(DeeplinkFetchProgress progress)
    {
        this.Dispatcher.Invoke(() =>
        {
            if (progress.TotalBytes is { } total && total > 0)
            {
                this.ProgressBarControl.IsIndeterminate = false;
                this.ProgressBarControl.Value = (double)progress.BytesRead / total;
            }
            else
            {
                this.ProgressBarControl.IsIndeterminate = true;
            }
        });
    }

    public void ShowError(string source, string message)
    {
        this.StatusText.Text = $"Couldn't open session from {source}:\n{message}";
        this.ProgressBarControl.Visibility = Visibility.Collapsed;
        this.CloseButton.Visibility = Visibility.Visible;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
