using System;
using System.Windows;

namespace MultiClod.App;

public partial class RenameDialog : Window
{
    private readonly Func<string, string?>? validate;

    public RenameDialog(string currentName, string title = "Rename Session", Func<string, string?>? validate = null)
    {
        this.InitializeComponent();
        this.Title = title;
        this.validate = validate;
        this.NameBox.Text = currentName;
        this.NameBox.SelectAll();
        this.Loaded += (_, _) => this.NameBox.Focus();
    }

    public string NewName { get; private set; } = string.Empty;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        var trimmed = this.NameBox.Text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        var error = this.validate?.Invoke(trimmed);
        if (error is not null)
        {
            this.ErrorText.Text = error;
            this.ErrorText.Visibility = Visibility.Visible;
            return;
        }

        this.NewName = trimmed;
        this.DialogResult = true;
    }
}
