using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Minimal read-only plain-text viewer for the "Other files" tab - unlike TranscriptViewerControl,
/// these files are arbitrary (not Claude JSONL transcripts), so content is shown as-is with no row
/// parsing/rendering.
/// </summary>
public partial class PlainTextFileViewer : UserControl
{
    private const int MaxDisplayChars = 5 * 1024 * 1024;

    public PlainTextFileViewer()
    {
        this.InitializeComponent();
    }

    public void SetSource(string filePath)
    {
        this.TruncationNotice.Visibility = Visibility.Collapsed;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
            {
                this.ContentTextBox.Text = "(file not found)";
                return;
            }

            if (info.Length > MaxDisplayChars)
            {
                using var reader = new StreamReader(filePath);
                var buffer = new char[MaxDisplayChars];
                var read = reader.Read(buffer, 0, buffer.Length);
                this.ContentTextBox.Text = new string(buffer, 0, read);
                this.TruncationNotice.Text = $"Showing the first {MaxDisplayChars / (1024 * 1024)} MB of a larger file.";
                this.TruncationNotice.Visibility = Visibility.Visible;
            }
            else
            {
                this.ContentTextBox.Text = File.ReadAllText(filePath);
            }
        }
        catch (IOException ex)
        {
            this.ContentTextBox.Text = $"(could not read file: {ex.Message})";
        }
    }
}
